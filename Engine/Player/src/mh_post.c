/* Post-processing, ported from the editor's GLPostProcessor: depth of field, motion blur,
   Panini projection, bloom, then the uber pass that writes the displayed image. */
#include <stdlib.h>
#include "mh_data.h"

struct mh_post_state {
    mh_rt_pool pool;
    GLuint coc_gaussian, coc_bokeh, dof_prefilter, dof_gaussian_blur, dof_gaussian_composite, dof_bokeh,
        dof_post_filter, dof_bokeh_composite, motion_blur, panini, bloom_prefilter, bloom_blur_h, bloom_blur_v,
        bloom_upsample, uber;
    GLuint curve_texture, white_texture;
    uint32_t random;
};

static void mh_post_init(mh_renderer *r)
{
    struct mh_post_state *p = calloc(1, sizeof *p);
    if (!p)
        exit(1);
    r->post = p;
    const char *v = mh_shader_PostProcessShaders_FullscreenVertex;
    p->coc_gaussian = mh_program(v, mh_shader_PostProcessShaders_CocGaussian, "CocGaussian");
    p->coc_bokeh = mh_program(v, mh_shader_PostProcessShaders_CocBokeh, "CocBokeh");
    p->dof_prefilter = mh_program(v, mh_shader_PostProcessShaders_DofPrefilter, "DofPrefilter");
    p->dof_gaussian_blur = mh_program(v, mh_shader_PostProcessShaders_DofGaussianBlur, "DofGaussianBlur");
    p->dof_gaussian_composite = mh_program(v, mh_shader_PostProcessShaders_DofGaussianComposite, "DofGaussianComposite");
    p->dof_bokeh = mh_program(v, mh_shader_PostProcessShaders_DofBokeh, "DofBokeh");
    p->dof_post_filter = mh_program(v, mh_shader_PostProcessShaders_DofPostFilter, "DofPostFilter");
    p->dof_bokeh_composite = mh_program(v, mh_shader_PostProcessShaders_DofBokehComposite, "DofBokehComposite");
    p->motion_blur = mh_program(v, mh_shader_PostProcessShaders_MotionBlur, "MotionBlur");
    p->panini = mh_program(v, mh_shader_PostProcessShaders_Panini, "Panini");
    p->bloom_prefilter = mh_program(v, mh_shader_PostProcessShaders_BloomPrefilter, "BloomPrefilter");
    p->bloom_blur_h = mh_program(v, mh_shader_PostProcessShaders_BloomBlurH, "BloomBlurH");
    p->bloom_blur_v = mh_program(v, mh_shader_PostProcessShaders_BloomBlurV, "BloomBlurV");
    p->bloom_upsample = mh_program(v, mh_shader_PostProcessShaders_BloomUpsample, "BloomUpsample");
    p->uber = mh_program(v, mh_shader_PostProcessShaders_Uber, "Uber");
    p->curve_texture = mh_texture(MH_CURVE_SAMPLES, 8, GL_R16F, GL_RED, GL_FLOAT, NULL, 0, 1);
    static const unsigned char white[4] = { 255, 255, 255, 255 };
    p->white_texture = mh_texture(1, 1, GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, white, 0, 0);
    p->random = 0x9E3779B9u;
}

/* 0..1, for the grain offset and the dither seed (xorshift). */
static float mh_random(struct mh_post_state *p)
{
    p->random ^= p->random << 13;
    p->random ^= p->random >> 17;
    p->random ^= p->random << 5;
    return (float)(p->random >> 8) / 16777216.0f;
}

static void mh_blit(GLuint framebuffer, int width, int height)
{
    glBindFramebuffer(GL_FRAMEBUFFER, framebuffer);
    glViewport(0, 0, width, height);
    glDrawArrays(GL_TRIANGLES, 0, 3);
}

static void mh_blit_rt(const mh_rt *rt) { mh_blit(rt->framebuffer, rt->width, rt->height); }

static vec2 mh_texel(int width, int height) { return v2(1.0f / (float)width, 1.0f / (float)height); }

/* Largest bokeh blur radius, as a fraction of the screen height (PostProcessMath.MaxBokehRadius). */
static float mh_max_bokeh_radius(int height) { return fminf(0.05f, 14.0f / (float)maxi(1, height)); }

static GLuint mh_depth_of_field(mh_renderer *r, const mh_view *view, GLuint source, const mh_post *s)
{
    struct mh_post_state *p = r->post;
    int width = r->width, height = r->height;
    int half_width = maxi(1, width / 2), half_height = maxi(1, height / 2);
    vec2 half_texel = mh_texel(half_width, half_height);
    int bokeh = s->dof_mode == 2;
    float max_coc = mh_max_bokeh_radius(height);

    /* Circle of confusion, full resolution. */
    mh_rt *coc = mh_pool_get(&p->pool, width, height, 1);
    mh_use(bokeh ? p->coc_bokeh : p->coc_gaussian);
    mh_set_texture("uDepth", 0, r->depth_texture);
    mh_set_f("uNear", view->near);
    mh_set_f("uFar", view->far);
    if (bokeh) {
        mh_set_f("uFocusDistance", s->dof_focus_distance);
        mh_set_f("uLensCoefficient", s->dof_lens_coefficient);
        mh_set_f("uMaxCoc", max_coc);
    } else {
        mh_set_f("uStart", s->dof_gaussian_start);
        mh_set_f("uEnd", s->dof_gaussian_end);
    }
    mh_blit_rt(coc);

    /* Half resolution color with the CoC in alpha. */
    mh_rt *prefiltered = mh_pool_get(&p->pool, half_width, half_height, 0);
    mh_use(p->dof_prefilter);
    mh_set_texture("uSource", 0, source);
    mh_set_texture("uCoc", 1, coc->texture);
    mh_set_i("uBokeh", bokeh);
    mh_set_f("uMaxCoc", max_coc);
    mh_set_v2("uTexelSize", half_texel);
    mh_blit_rt(prefiltered);

    mh_rt *result = mh_pool_get(&p->pool, width, height, 0);
    if (!bokeh) {
        /* Wider steps for a larger radius; high quality takes smaller steps (and twice the passes). */
        int passes = s->dof_high_quality ? 2 : 1;
        float step = s->dof_gaussian_max_radius * (s->dof_high_quality ? 1.5f : 2.5f);
        mh_rt *blurred = prefiltered;
        for (int i = 0; i < passes; i++) {
            mh_rt *horizontal = mh_pool_get(&p->pool, half_width, half_height, 0);
            mh_use(p->dof_gaussian_blur);
            mh_set_texture("uSource", 0, blurred->texture);
            mh_set_v2("uDirection", v2(half_texel.x * step, 0));
            mh_blit_rt(horizontal);

            mh_rt *vertical = mh_pool_get(&p->pool, half_width, half_height, 0);
            mh_set_texture("uSource", 0, horizontal->texture);
            mh_set_v2("uDirection", v2(0, half_texel.y * step));
            mh_blit_rt(vertical);
            blurred = vertical;
        }
        mh_use(p->dof_gaussian_composite);
        mh_set_texture("uSource", 0, source);
        mh_set_texture("uBlurred", 1, blurred->texture);
        mh_set_texture("uCoc", 2, coc->texture);
        mh_blit_rt(result);
        return result->texture;
    }

    mh_rt *gathered = mh_pool_get(&p->pool, half_width, half_height, 0);
    mh_use(p->dof_bokeh);
    mh_set_texture("uSource", 0, prefiltered->texture);
    mh_set_v2v("uKernel", s->dof_kernel, MH_BOKEH_KERNEL);
    mh_set_f("uMaxCoc", max_coc);
    mh_set_f("uRcpAspect", (float)height / (float)width);
    mh_set_v2("uTexelSize", half_texel);
    mh_blit_rt(gathered);

    mh_rt *smoothed = mh_pool_get(&p->pool, half_width, half_height, 0);
    mh_use(p->dof_post_filter);
    mh_set_texture("uSource", 0, gathered->texture);
    mh_set_v2("uTexelSize", half_texel);
    mh_blit_rt(smoothed);

    mh_use(p->dof_bokeh_composite);
    mh_set_texture("uSource", 0, source);
    mh_set_texture("uBlurred", 1, smoothed->texture);
    mh_set_texture("uCoc", 2, coc->texture);
    mh_set_f("uMaxCoc", max_coc);
    mh_set_v2("uTexelSize", half_texel);
    mh_blit_rt(result);
    return result->texture;
}

static GLuint mh_motion_blur(mh_renderer *r, mat4 view_projection, GLuint source, const mh_post *s)
{
    struct mh_post_state *p = r->post;
    mat4 inverse;
    m4_invert(view_projection, &inverse);
    mh_rt *result = mh_pool_get(&p->pool, r->width, r->height, 0);
    mh_use(p->motion_blur);
    mh_set_texture("uSource", 0, source);
    mh_set_texture("uDepth", 1, r->depth_texture);
    mh_set_m4("uInverseViewProjection", inverse);
    mh_set_m4("uPreviousViewProjection", r->previous_view_projection);
    mh_set_f("uIntensity", s->motion_intensity);
    mh_set_f("uClamp", s->motion_clamp);
    mh_set_i("uSamples", s->motion_samples);
    mh_blit_rt(result);
    return result->texture;
}

/* PostProcessMath.Panini: view extents x, y, distance, crop scale. */
static vec4 mh_panini_params(float distance, float crop_to_fit, float fov_y, float aspect)
{
    float tan_half = tanf(fov_y * 0.5f);
    vec2 extents = v2(tan_half * aspect, tan_half);
    float d = distance;
    float proj_hyp = sqrtf(extents.x * extents.x + 1.0f);
    float cyl_dist_minus_d = 1.0f / proj_hyp;
    float cyl_dist = cyl_dist_minus_d + d;
    float k = cyl_dist_minus_d * ((1.0f + d) / cyl_dist);
    vec2 crop = v2(extents.x * k, extents.y * k);
    float scale = fminf(crop.x / extents.x, crop.y / extents.y);
    float panini_s = 1.0f + (clampf(scale, 0.0f, 1.0f) - 1.0f) * crop_to_fit;
    return v4(extents.x, extents.y, d, panini_s);
}

static GLuint mh_panini(mh_renderer *r, const mh_view *view, GLuint source, const mh_post *s)
{
    struct mh_post_state *p = r->post;
    mh_rt *result = mh_pool_get(&p->pool, r->width, r->height, 0);
    mh_use(p->panini);
    mh_set_texture("uSource", 0, source);
    mh_set_v4("uParams", mh_panini_params(s->panini_distance, s->panini_crop, view->fov_y,
                                          (float)r->width / (float)r->height));
    mh_blit_rt(result);
    return result->texture;
}

static mh_rt *mh_bloom(mh_renderer *r, GLuint source, const mh_post *s)
{
    struct mh_post_state *p = r->post;
    int width = r->width, height = r->height;
    int mip_width = maxi(1, width / s->bloom_downscale), mip_height = maxi(1, height / s->bloom_downscale);

    /* As many mips as the size allows, at most the maximum iterations. */
    int max_size = maxi(mip_width, mip_height);
    int iterations = clampi((int)floorf(log2f((float)max_size)) - 1, 1, s->bloom_max_iterations);

    mh_rt *mips[16];
    mips[0] = mh_pool_get(&p->pool, mip_width, mip_height, 0);
    mh_use(p->bloom_prefilter);
    mh_set_texture("uSource", 0, source);
    mh_set_v2("uSourceTexelSize", mh_texel(width, height));
    mh_set_f("uThreshold", s->bloom_threshold);
    mh_set_f("uKnee", s->bloom_knee);
    mh_set_f("uClampMax", s->bloom_clamp);
    mh_blit_rt(mips[0]);

    /* Downsample: horizontal blur into the half size, then vertical blur. */
    for (int i = 1; i < iterations; i++) {
        mh_rt *previous = mips[i - 1];
        int w = maxi(1, previous->width / 2), h = maxi(1, previous->height / 2);
        mh_rt *horizontal = mh_pool_get(&p->pool, w, h, 0);
        mh_use(p->bloom_blur_h);
        mh_set_texture("uSource", 0, previous->texture);
        mh_set_v2("uSourceTexelSize", mh_texel(previous->width, previous->height));
        mh_blit_rt(horizontal);

        mips[i] = mh_pool_get(&p->pool, w, h, 0);
        mh_use(p->bloom_blur_v);
        mh_set_texture("uSource", 0, horizontal->texture);
        mh_set_v2("uSourceTexelSize", mh_texel(w, h));
        mh_blit_rt(mips[i]);
    }

    /* Upsample: blend each mip with the (already combined) smaller one. */
    mh_rt *lower = mips[iterations - 1];
    for (int i = iterations - 2; i >= 0; i--) {
        mh_rt *combined = mh_pool_get(&p->pool, mips[i]->width, mips[i]->height, 0);
        mh_use(p->bloom_upsample);
        mh_set_texture("uSource", 0, mips[i]->texture);
        mh_set_texture("uLowMip", 1, lower->texture);
        mh_set_v2("uLowMipTexelSize", mh_texel(lower->width, lower->height));
        mh_set_f("uScatter", s->bloom_scatter);
        mh_set_i("uHighQuality", s->bloom_high_quality);
        mh_blit_rt(combined);
        lower = combined;
    }
    return lower;
}

static void mh_uber(mh_renderer *r, GLuint source, const mh_rt *bloom, const mh_post *s)
{
    struct mh_post_state *p = r->post;
    mh_use(p->uber);
    mh_set_texture("uSource", 0, source);
    mh_set_v2("uScreenSize", v2((float)r->width, (float)r->height));

    mh_set_i("uBloomOn", bloom != NULL);
    mh_set_texture("uBloom", 1, bloom ? bloom->texture : p->white_texture);
    if (bloom) {
        mh_set_i("uBloomHighQuality", s->bloom_high_quality);
        mh_set_v2("uBloomTexelSize", mh_texel(bloom->width, bloom->height));
        mh_set_f("uBloomIntensity", s->bloom_intensity);
        mh_set_v3("uBloomTint", s->bloom_tint);
    }

    /* Lens distortion, chromatic aberration and color grading, computed by the editor. */
    for (int i = 0; i < s->uniform_count; i++)
        mh_set_uniform(&s->uniforms[i]);
    if (s->curves_on) {
        glBindTexture(GL_TEXTURE_2D, p->curve_texture);
        glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, MH_CURVE_SAMPLES, 8, GL_RED, GL_FLOAT, s->curves);
    }
    mh_set_texture("uCurves", 2, p->curve_texture);

    mh_set_i("uVignetteOn", s->vignette_on);
    if (s->vignette_on) {
        mh_set_v3("uVignetteColor", s->vignette_color);
        mh_set_v2("uVignetteCenter", s->vignette_center);
        mh_set_v3("uVignetteParams", v3(s->vignette_intensity, s->vignette_smoothness,
                                        s->vignette_rounded ? (float)r->width / (float)r->height : 1.0f));
    }

    const mh_gpu_texture *lut = mh_renderer_texture(r, s->lut_texture);
    mh_set_i("uLutOn", lut != NULL);
    mh_set_texture("uLut", 3, lut ? lut->texture : p->white_texture);
    if (lut)
        mh_set_v4("uLutParams", v4(1.0f / (float)lut->width, 1.0f / (float)lut->height, (float)(lut->height - 1),
                                   s->lut_contribution));

    const mh_gpu_texture *grain = mh_renderer_texture(r, s->grain_texture);
    mh_set_i("uGrainOn", grain != NULL);
    mh_set_texture("uGrain", 4, grain ? grain->texture : p->white_texture);
    if (grain) {
        mh_set_v2("uGrainParams", v2(s->grain_intensity, s->grain_response));
        float x = mh_random(p), y = mh_random(p);
        mh_set_v4("uGrainScaleOffset", v4((float)r->width / (float)grain->width, (float)r->height / (float)grain->height, x, y));
    }
    mh_set_f("uDitherSeed", mh_random(p) * 100.0f);

    mh_blit(r->output_framebuffer, r->width, r->height);
}

static void mh_post_run(mh_renderer *r, const mh_view *view, const mh_post *s)
{
    glDisable(GL_DEPTH_TEST);
    glDisable(GL_CULL_FACE);
    glDisable(GL_BLEND);
    glDepthMask(GL_FALSE);
    glBindVertexArray(r->empty_vao);

    mat4 view_projection = m4_mul(view->view, view->projection);
    GLuint source = r->hdr_texture;
    if (s->dof_mode != 0)
        source = mh_depth_of_field(r, view, source, s);
    if (s->motion_on && r->has_previous_view_projection)
        source = mh_motion_blur(r, view_projection, source, s);
    if (s->panini_on)
        source = mh_panini(r, view, source, s);
    mh_rt *bloom = s->bloom_on ? mh_bloom(r, source, s) : NULL;
    mh_uber(r, source, bloom, s);

    r->previous_view_projection = view_projection;
    r->has_previous_view_projection = 1;
    glDepthMask(GL_TRUE);
    mh_pool_release_all(&r->post->pool);
}
