﻿using System;

namespace Campy.Compiler
{
    public class Runtime
    {
        // Arrays are implemented as a struct, with the data following the struct
        // in row major format. Note, each dimension has a length that is recorded
        // following the pointer p. The one shown here is for only one-dimensional
        // arrays.
        // Calls have to be casted to this type.
        public unsafe struct A
        {
            public void* p;
            public long d;
            public long l; // Width of dimension 0.
            // Additional widths for dimension 1, dimension 2, ...
            // Value data after all dimensional sizes.
        }

        public static unsafe int get_length_multi_array(A* arr, int i0)
        {
            byte* bp = (byte*)arr;
            bp = bp + 16 + 8 * i0;
            long* lp = (long*)bp;
            return (int) *lp;
        }

        public static unsafe int get_multi_array(A* arr, int i0)
        {
            int* a = *(int**)arr;
            return *(a + i0);
        }

        public static unsafe int get_multi_array(A* arr, int i0, int i1)
        {
            // (y * xMax) + x
            int* a = (int*)(*arr).p;
            int d = (int)(*arr).d;
            byte* d0_ptr = (byte*)arr;
            d0_ptr = d0_ptr + 24;
            long o = 0;
            long d0 = *(long*)d0_ptr;
            o = i0 * d0 + i1;
            return *(a + o);
        }

        public static unsafe int get_multi_array(A* arr, int i0, int i1, int i2)
        {
            // (z * xMax * yMax) + (y * xMax) + x;
            int* a = (int*)(*arr).p;
            int d = (int)(*arr).d;
            byte* bp_d0 = (byte*)arr;
            byte* bp_d1 = (byte*)arr;
            bp_d1 = bp_d1 + 24;
            long o = 0;
            long* lp_d1 = (long*)bp_d1;
            byte* bp_d2 = bp_d1 + 8;
            long* lp_d2 = (long*)bp_d2;
            o = (*lp_d1) * i0 + i1;
            return  *(a + o);
        }

        public static unsafe void set_multi_array(A* arr, int i0, int value)
        {
            int* a = (int*)(*arr).p;
            int d = (int)(*arr).d;
            long o = i0;
            *(a + o) = value;
        }

        public static unsafe void set_multi_array(A* arr, int i0, int i1, int value)
        {
            //  b[i, j] = j  + i * ex[1];

            int* a = (int*)(*arr).p;
            long ex1 = *(long*)(24 + (byte*)arr);
            long o = i1 + ex1 * i0;
            *(a + o) = value;
        }

        public static unsafe void set_multi_array(A* arr, int i0, int i1, int i2, int value)
        {
            //  b[i, j, k] = k + j * ex[2] + i * ex[2] * ex[1];

            int* a = (int*)(*arr).p;
            long ex1 = *(long*)(24 + (byte*)arr);
            long ex2 = *(long*)(32 + (byte*)arr);
            long o = i2 + i1 * ex2 + i0 * ex2 * ex1;
            *(a + o) = value;
        }

        public static double Sine(double x)
        {
            const double PI = 3.14159265358979323846264338327950288f;
            const double PI_SQR = 9.86960440108935861883449099987615114f;
            const double B = 4 / PI;
            const double C = -4 / PI_SQR;
            const double P = 0.225;

            double xp = x < 0 ? -x : x;
            double y = B * x + C * x * xp;
            double yp = y < 0 ? -y : y;
            y = P * (y * yp - y) + y;
            return y;
        }

        public static double Cosine(double x)
        {
            const double PID2 = 1.57079632679489661923132169163975144f;
            const double hpi = PID2;
            x += hpi; //shift for cosine
            return Sine(x);
        }

        public static double Abs(double x)
        {
            return x > 0 ? x : -x;
        }

        public static void ThrowArgumentOutOfRangeException()
        {
        }

    }
}
