using System;

namespace PartnerLive2D.Framework
{
    // 2D float vector (CubismVector2).
    internal struct Vec2
    {
        public float X, Y;
        public Vec2(float x, float y) { X = x; Y = y; }

        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.X + b.X, a.Y + b.Y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Y - b.Y); }
        public static Vec2 operator *(Vec2 v, float s) { return new Vec2(v.X * s, v.Y * s); }
        public static Vec2 operator /(Vec2 v, float s) { return new Vec2(v.X / s, v.Y / s); }

        // CubismVector2::Normalize. A zero vector is left untouched (C++ would produce NaN).
        public void Normalize()
        {
            float length = (float)Math.Pow((double)(X * X + Y * Y), 0.5);
            if (length == 0f) return;
            X = X / length;
            Y = Y / length;
        }
    }

    // Subset of CubismMath used by the ported classes. All math in float like the C++ core.
    internal static class CMath
    {
        public const float Pi = 3.1415926535897932384626433832795f;
        public const float Epsilon = 0.00001f;

        public static float Sin(float x) { return (float)Math.Sin((double)x); }
        public static float Cos(float x) { return (float)Math.Cos((double)x); }
        public static float Sqrt(float x) { return (float)Math.Sqrt((double)x); }
        public static float Abs(float x) { return x < 0f ? -x : x; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }

        public static float Cbrt(float x)
        {
            return x < 0f ? -(float)Math.Pow((double)(-x), 1.0 / 3.0) : (float)Math.Pow((double)x, 1.0 / 3.0);
        }

        public static float Range(float v, float min, float max)
        {
            if (v < min) return min;
            if (max < v) return max;
            return v;
        }

        public static float GetEasingSine(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return 0.5f - 0.5f * Cos(value * Pi);
        }

        public static float DegreesToRadian(float degrees) { return (degrees / 180.0f) * Pi; }

        public static float DirectionToRadian(Vec2 from, Vec2 to)
        {
            float q1 = (float)Math.Atan2((double)to.Y, (double)to.X);
            float q2 = (float)Math.Atan2((double)from.Y, (double)from.X);
            float ret = q1 - q2;
            while (ret < -Pi) ret += Pi * 2.0f;
            while (ret > Pi) ret -= Pi * 2.0f;
            return ret;
        }

        public static Vec2 RadianToDirection(float totalAngle)
        {
            return new Vec2(Sin(totalAngle), Cos(totalAngle));
        }

        public static float QuadraticEquation(float a, float b, float c)
        {
            if (Abs(a) < Epsilon)
            {
                if (Abs(b) < Epsilon) return -c;
                return -c / b;
            }
            return -(b + Sqrt(b * b - 4.0f * a * c)) / (2.0f * a);
        }

        public static float CardanoAlgorithmForBezier(float a, float b, float c, float d)
        {
            if (Abs(a) < Epsilon)
                return Range(QuadraticEquation(b, c, d), 0.0f, 1.0f);

            float ba = b / a;
            float ca = c / a;
            float da = d / a;

            float p = (3.0f * ca - ba * ba) / 3.0f;
            float p3 = p / 3.0f;
            float q = (2.0f * ba * ba * ba - 9.0f * ba * ca + 27.0f * da) / 27.0f;
            float q2 = q / 2.0f;
            float discriminant = q2 * q2 + p3 * p3 * p3;

            const float center = 0.5f;
            const float threshold = center + 0.01f;

            if (discriminant < 0.0f)
            {
                float mp3 = -p / 3.0f;
                float mp33 = mp3 * mp3 * mp3;
                float r = Sqrt(mp33);
                float t = -q / (2.0f * r);
                float cosphi = Range(t, -1.0f, 1.0f);
                float phi = (float)Math.Acos((double)cosphi);
                float crtr = Cbrt(r);
                float t1 = 2.0f * crtr;

                float root1 = t1 * Cos(phi / 3.0f) - ba / 3.0f;
                if (Abs(root1 - center) < threshold) return Range(root1, 0.0f, 1.0f);

                float root2 = t1 * Cos((phi + 2.0f * Pi) / 3.0f) - ba / 3.0f;
                if (Abs(root2 - center) < threshold) return Range(root2, 0.0f, 1.0f);

                float root3 = t1 * Cos((phi + 4.0f * Pi) / 3.0f) - ba / 3.0f;
                return Range(root3, 0.0f, 1.0f);
            }

            if (discriminant == 0.0f)
            {
                float u1;
                if (q2 < 0.0f) u1 = Cbrt(-q2);
                else u1 = -Cbrt(q2);

                float root1 = 2.0f * u1 - ba / 3.0f;
                if (Abs(root1 - center) < threshold) return Range(root1, 0.0f, 1.0f);

                float root2 = -u1 - ba / 3.0f;
                return Range(root2, 0.0f, 1.0f);
            }

            float sd = Sqrt(discriminant);
            float u = Cbrt(sd - q2);
            float v1 = Cbrt(sd + q2);
            float root = u - v1 - ba / 3.0f;
            return Range(root, 0.0f, 1.0f);
        }
    }
}
