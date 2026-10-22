/* Turns the object around an axis; Space reverses it, and R gives it a quick extra turn. */
MH_CREATE Spinner.c

MH_VARS {
    float speed = 45;           // MH_RANGE(0, 360) Degrees per second
    MH_VEC3 axis = MH_VEC3(0, 1, 0);
};

MH_UPDATE()
{
    if (MH_KEY_DOWN("space"))
        self.speed = -self.speed;
    if (MH_KEY_DOWN("r") && !MH_IS_TWEENING(MH_PARENT))
        MH_ROTATE(MH_PARENT, MH_VEC3(0, 360, 0), 0.6f, MH_TWEEN_TYPE(MH_BACK_OUT));
    MH_SPIN(MH_PARENT, mh_v3_scale(self.axis, self.speed));
}
