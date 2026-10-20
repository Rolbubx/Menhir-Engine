/* Menhir runtime: allocation, type checks, errors, and the corlib's internal calls. */
#include "menhir/runtime.h"

#include <gc.h>
#include <stdio.h>
#include <stdlib.h>

/* ---- Startup -------------------------------------------------------------------------------- */

void m_init(void)
{
    GC_INIT();
}

MArray* m_make_args(int argc, char** argv, const MType* stringArrayType)
{
    int count = argc > 1 ? argc - 1 : 0;
    MArray* args = m_new_array(stringArrayType, count);
    MString** strings = (MString**)M_ARRAY_DATA(args);
    for (int i = 0; i < count; i++)
        strings[i] = m_string_from_utf8(argv[i + 1]);
    return args;
}

/* ---- Allocation ----------------------------------------------------------------------------- */

static void* m_alloc(size_t size, bool noReferences)
{
    void* memory = noReferences ? GC_MALLOC_ATOMIC(size) : GC_MALLOC(size);
    if (memory == NULL) {
        fputs("Out of memory.\n", stderr);
        exit(134);
    }
    /* GC_MALLOC clears the memory, GC_MALLOC_ATOMIC does not. */
    if (noReferences)
        memset(memory, 0, size);
    return memory;
}

MObject* m_new_object(const MType* type)
{
    MObject* object = m_alloc(type->instanceSize, (type->flags & M_TYPE_NO_REFERENCES) != 0);
    object->type = type;
    return object;
}

MArray* m_new_array(const MType* arrayType, intptr_t length)
{
    if (length < 0 || length > INT32_MAX)
        m_throw_overflow();
    const MType* element = arrayType->elementType;
    bool noReferences = (element->flags & M_TYPE_VALUE_TYPE) && (element->flags & M_TYPE_NO_REFERENCES);
    size_t size = offsetof(MArray, data) + (size_t)length * arrayType->elementSize;
    MArray* array = m_alloc(size, noReferences);
    array->header.type = arrayType;
    array->length = (int32_t)length;
    return array;
}

MString* m_new_string(int32_t length)
{
    if (length < 0)
        m_throw_overflow();
    /* One extra character keeps a terminating zero, handy when passing text to C. */
    MString* s = m_alloc(offsetof(MString, chars) + ((size_t)length + 1) * sizeof(uint16_t), true);
    s->header.type = &System_String__type;
    s->length = length;
    return s;
}

MString* m_string_from_utf8(const char* text)
{
    /* Decode UTF-8 into UTF-16 (two passes: count, then fill). */
    const unsigned char* p = (const unsigned char*)text;
    int32_t units = 0;
    for (const unsigned char* q = p; *q; ) {
        uint32_t c = *q;
        int extra = c >= 0xF0 ? 3 : c >= 0xE0 ? 2 : c >= 0xC0 ? 1 : 0;
        units += extra == 3 ? 2 : 1;
        q += 1 + extra;
    }
    MString* s = m_new_string(units);
    int32_t i = 0;
    while (*p) {
        uint32_t c = *p++;
        if (c >= 0xF0) { c = (c & 0x07) << 18; c |= (*p++ & 0x3F) << 12; c |= (*p++ & 0x3F) << 6; c |= *p++ & 0x3F; }
        else if (c >= 0xE0) { c = (c & 0x0F) << 12; c |= (*p++ & 0x3F) << 6; c |= *p++ & 0x3F; }
        else if (c >= 0xC0) { c = (c & 0x1F) << 6; c |= *p++ & 0x3F; }
        if (c >= 0x10000) {
            c -= 0x10000;
            s->chars[i++] = (uint16_t)(0xD800 + (c >> 10));
            s->chars[i++] = (uint16_t)(0xDC00 + (c & 0x3FF));
        } else {
            s->chars[i++] = (uint16_t)c;
        }
    }
    return s;
}

MObject* m_box(const MType* valueType, const void* value)
{
    MObject* box = m_alloc(offsetof(MBox, data) + valueType->instanceSize,
                           (valueType->flags & M_TYPE_NO_REFERENCES) != 0);
    box->type = valueType;
    memcpy(M_BOX_DATA(box), value, valueType->instanceSize);
    return box;
}

/* ---- Type checks ---------------------------------------------------------------------------- */

bool m_is_subclass(const MType* type, const MType* target)
{
    for (const MType* t = type; t != NULL; t = t->base) {
        if (t == target)
            return true;
    }
    /* Arrays of references are covariant: string[] is an object[]. */
    if ((type->flags & M_TYPE_ARRAY) && (target->flags & M_TYPE_ARRAY)) {
        const MType* from = type->elementType;
        const MType* to = target->elementType;
        if (!(from->flags & M_TYPE_VALUE_TYPE) && !(to->flags & M_TYPE_VALUE_TYPE))
            return m_is_subclass(from, to);
    }
    return false;
}

MObject* m_isinst(MObject* object, const MType* target)
{
    return object != NULL && m_is_subclass(object->type, target) ? object : NULL;
}

MObject* m_castclass(MObject* object, const MType* target)
{
    if (object != NULL && !m_is_subclass(object->type, target)) {
        char message[512];
        snprintf(message, sizeof message, "Unable to cast object of type '%s' to type '%s'.", object->type->name, target->name);
        m_throw_named("System.InvalidCastException", message);
    }
    return object;
}

void* m_unbox(MObject* object, const MType* valueType)
{
    if (object == NULL)
        m_throw_null();
    if (object->type != valueType)
        m_throw_invalid_cast();
    return M_BOX_DATA(object);
}

void m_check_array_store(MArray* array, MObject* value)
{
    if (value != NULL && !m_is_subclass(value->type, array->header.type->elementType))
        m_throw_named("System.ArrayTypeMismatchException", "Attempted to access an element as a type incompatible with the array.");
}

/* ---- Errors --------------------------------------------------------------------------------- */

static void m_write_utf16(FILE* stream, const uint16_t* chars, int32_t length);

/* System.Exception's first field is its message (see Translator/CoreLib/System/Exception.cs). */
typedef struct MException {
    MObject header;
    MString* message;
} MException;

_Noreturn void m_throw(MObject* exception)
{
    if (exception == NULL)
        m_throw_null();
    fflush(stdout);
    fprintf(stderr, "Unhandled exception. %s", exception->type->name);
    MString* message = ((MException*)exception)->message;
    if (message != NULL) {
        fputs(": ", stderr);
        m_write_utf16(stderr, message->chars, message->length);
    }
    fputs("\n", stderr);
    exit(134);
}

_Noreturn void m_throw_named(const char* typeName, const char* message)
{
    fflush(stdout);
    fprintf(stderr, "Unhandled exception. %s: %s\n", typeName, message);
    exit(134);
}

_Noreturn void m_throw_null(void)
{
    m_throw_named("System.NullReferenceException", "Object reference not set to an instance of an object.");
}

_Noreturn void m_throw_index(void)
{
    m_throw_named("System.IndexOutOfRangeException", "Index was outside the bounds of the array.");
}

_Noreturn void m_throw_overflow(void)
{
    m_throw_named("System.OverflowException", "Arithmetic operation resulted in an overflow.");
}

_Noreturn void m_throw_divide_by_zero(void)
{
    m_throw_named("System.DivideByZeroException", "Attempted to divide by zero.");
}

_Noreturn void m_throw_invalid_cast(void)
{
    m_throw_named("System.InvalidCastException", "Specified cast is not valid.");
}

/* ---- Text output ---------------------------------------------------------------------------- */

static void m_write_utf16(FILE* stream, const uint16_t* chars, int32_t length)
{
    char buffer[512];
    int used = 0;
    for (int32_t i = 0; i < length; i++) {
        uint32_t c = chars[i];
        if (c >= 0xD800 && c <= 0xDBFF && i + 1 < length && chars[i + 1] >= 0xDC00 && chars[i + 1] <= 0xDFFF)
            c = 0x10000 + ((c - 0xD800) << 10) + (chars[++i] - 0xDC00);
        else if (c >= 0xD800 && c <= 0xDFFF)
            c = 0xFFFD; /* lone surrogate */
        if (used > (int)sizeof buffer - 4) {
            fwrite(buffer, 1, used, stream);
            used = 0;
        }
        if (c < 0x80) {
            buffer[used++] = (char)c;
        } else if (c < 0x800) {
            buffer[used++] = (char)(0xC0 | (c >> 6));
            buffer[used++] = (char)(0x80 | (c & 0x3F));
        } else if (c < 0x10000) {
            buffer[used++] = (char)(0xE0 | (c >> 12));
            buffer[used++] = (char)(0x80 | ((c >> 6) & 0x3F));
            buffer[used++] = (char)(0x80 | (c & 0x3F));
        } else {
            buffer[used++] = (char)(0xF0 | (c >> 18));
            buffer[used++] = (char)(0x80 | ((c >> 12) & 0x3F));
            buffer[used++] = (char)(0x80 | ((c >> 6) & 0x3F));
            buffer[used++] = (char)(0x80 | (c & 0x3F));
        }
    }
    fwrite(buffer, 1, used, stream);
}

/* ---- Number formatting ---------------------------------------------------------------------- */

/*
 * Formats like .NET's double.ToString() / float.ToString() with the invariant culture: the
 * shortest digits that read back as the same value, in scientific notation ("1.5E+20") when
 * the decimal exponent is at least 17 for double (9 for float, their round-trip precisions) or
 * below -4 ("1E-05").
 */
static MString* m_format_float(double value, bool single)
{
    char out[64];
    if (value != value)
        return m_string_from_utf8("NaN");
    if (isinf(value))
        return m_string_from_utf8(value > 0 ? "Infinity" : "-Infinity");

    /* Shortest round-tripping digits from "%.*e" (d.ddde±x). */
    char text[64];
    int maxDigits = single ? 9 : 17;
    for (int digits = 1; digits <= maxDigits; digits++) {
        snprintf(text, sizeof text, "%.*e", digits - 1, value);
        if (single ? (float)strtod(text, NULL) == (float)value : strtod(text, NULL) == value)
            break;
    }

    bool negative = text[0] == '-';
    char digitChars[32];
    int count = 0;
    const char* p = text + (negative ? 1 : 0);
    for (; *p && *p != 'e'; p++) {
        if (*p >= '0' && *p <= '9')
            digitChars[count++] = *p;
    }
    int exponent = atoi(p + 1);
    while (count > 1 && digitChars[count - 1] == '0')
        count--;
    if (count == 1 && digitChars[0] == '0')
        exponent = 0;

    int precision = single ? 9 : 17;
    int n = 0;
    if (negative)
        out[n++] = '-';
    if (exponent >= precision || exponent < -4) {
        out[n++] = digitChars[0];
        if (count > 1) {
            out[n++] = '.';
            for (int i = 1; i < count; i++)
                out[n++] = digitChars[i];
        }
        n += snprintf(out + n, sizeof out - n, "E%c%02d", exponent < 0 ? '-' : '+', exponent < 0 ? -exponent : exponent);
    } else if (exponent >= 0) {
        for (int i = 0; i <= exponent || i < count; i++) {
            if (i == exponent + 1)
                out[n++] = '.';
            out[n++] = i < count ? digitChars[i] : '0';
        }
    } else {
        out[n++] = '0';
        out[n++] = '.';
        for (int i = 0; i < -exponent - 1; i++)
            out[n++] = '0';
        for (int i = 0; i < count; i++)
            out[n++] = digitChars[i];
    }
    out[n] = 0;
    return m_string_from_utf8(out);
}

/* ---- Internal calls of Menhir.CoreLib ------------------------------------------------------- */
/* Names follow the translator's scheme for InternalCall methods: <Namespace_Type>__<Method>. */

MString* System_Object__GetTypeName(MObject* self)
{
    return m_string_from_utf8(self->type->name);
}

int32_t System_Runtime_CompilerServices_RuntimeHelpers__GetHashCode(MObject* object)
{
    /* Objects never move (Boehm), so their address is a stable identity. */
    uintptr_t address = (uintptr_t)object;
    return (int32_t)(uint32_t)(address >> 3 ^ address >> 35);
}

MString* System_String__FastAllocate(int32_t length)
{
    return m_new_string(length);
}

void System_Console__WriteString(MString* value)
{
    m_write_utf16(stdout, value->chars, value->length);
}

MString* System_Number__DoubleToString(double value)
{
    return m_format_float(value, false);
}

MString* System_Number__SingleToString(float value)
{
    return m_format_float(value, true);
}

void System_ThrowHelpers__IndexOutOfRange(void)
{
    m_throw_index();
}

void System_ThrowHelpers__ArgumentOutOfRange(void)
{
    m_throw_named("System.ArgumentOutOfRangeException", "Specified argument was out of the range of valid values.");
}

double System_Math__Sqrt(double d) { return sqrt(d); }
double System_Math__Sin(double a) { return sin(a); }
double System_Math__Cos(double d) { return cos(d); }
double System_Math__Tan(double a) { return tan(a); }
double System_Math__Atan2(double y, double x) { return atan2(y, x); }
double System_Math__Pow(double x, double y) { return pow(x, y); }
double System_Math__Exp(double d) { return exp(d); }
double System_Math__Log(double d) { return log(d); }
double System_Math__Floor(double d) { return floor(d); }
double System_Math__Ceiling(double a) { return ceil(a); }
/* Math.Round rounds half to even by default. */
double System_Math__Round(double a) { return nearbyint(a); }
