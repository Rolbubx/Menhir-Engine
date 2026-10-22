/* Floats the object up and down around where it starts. */
MH_CREATE Bobber.c

MH_VARS {
    float height = 0.25f;
    float period = 2;       // Seconds for a full bob
    MH_VEC3 start;          // MH_HIDE
};

MH_START()
{
    self.start = MH_GET_POSITION(MH_PARENT);
    MH_LOG("%s bobs around (%.2f, %.2f, %.2f)", MH_NAME(MH_PARENT), self.start.x, self.start.y, self.start.z);
}

MH_UPDATE()
{
    float phase = MH_TIME() / self.period * 2 * MH_PI;
    MH_SET_POSITION(MH_PARENT, mh_v3_add(self.start, MH_VEC3(0, sinf(phase) * self.height, 0)));
}
