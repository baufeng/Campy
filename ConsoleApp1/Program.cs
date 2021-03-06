﻿using System;
using System.Collections.Generic;
using System.Linq;
using Campy;
using System.Numerics;
using Mono.Cecil;

namespace ConsoleApp1
{
    public class FFT
    {
        /* Performs a Bit Reversal Algorithm on a postive integer 
         * for given number of bits
         * e.g. 011 with 3 bits is reversed to 110
         */
        public static int BitReverse(int n, int bits)
        {
            int reversedN = n;
            int count = bits - 1;

            n >>= 1;
            while (n > 0)
            {
                reversedN = (reversedN << 1) | (n & 1);
                count--;
                n >>= 1;
            }

            return ((reversedN << count) & ((1 << bits) - 1));
        }

        /* Uses Cooley-Tukey iterative in-place algorithm with radix-2 DIT case
         * assumes no of points provided are a power of 2 */
        public static void Seq(Complex[] buffer)
        {
            int bits = (int)Math.Log(buffer.Length, 2);
            for (int j = 1; j < buffer.Length / 2; j++)
            {
                int swapPos = BitReverse(j, bits);
                var temp = buffer[j];
                buffer[j] = buffer[swapPos];
                buffer[swapPos] = temp;
            }

            for (int N = 2; N <= buffer.Length; N <<= 1)
            {
                for (int i = 0; i < buffer.Length; i += N)
                {
                    for (int k = 0; k < N / 2; k++)
                    {
                        int evenIndex = i + k;
                        int oddIndex = i + k + (N / 2);
                        var even = buffer[evenIndex];
                        var odd = buffer[oddIndex];

                        double term = -2 * Math.PI * k / (double)N;
                        Complex exp = new Complex(Math.Cos(term), Math.Sin(term)) * odd;

                        buffer[evenIndex] = even + exp;
                        buffer[oddIndex] = even - exp;
                    }
                }
            }
        }

        public static void Par(Complex[] buffer)
        {
            Campy.Parallel.Sticky(buffer);

            int bits = (int)Math.Log(buffer.Length, 2);
            Campy.Parallel.For(buffer.Length / 2 - 1, k =>
            {
                int j = k + 1;
                int swapPos = BitReverse(j, bits);
                var temp = buffer[j];
                buffer[j] = buffer[swapPos];
                buffer[swapPos] = temp;
            });

            for (int N = 2; N <= buffer.Length; N <<= 1)
            {
                int step = N / 2;
                Campy.Parallel.For(buffer.Length / 2, d =>
                {
                    var k = d % step;
                    var t = d % step + N * (d / step);
                    int evenIndex = t;
                    int oddIndex = t + step;

                    var even = buffer[evenIndex];
                    var odd = buffer[oddIndex];

                    double term = -2 * Math.PI * k / (double)N;
                    Complex exp = new Complex(Math.Cos(term), Math.Sin(term)) * odd;

                    buffer[evenIndex] = even + exp;
                    buffer[oddIndex] = even - exp;
                });
            }
            Campy.Parallel.Sync();
        }
    }

    public class Test
    {
        static bool ApproxEqual(double a, double b)
        {
            return b - a < 0.0001 || a - b < 0.0001;
        }

        public void FFT_Test()
        {
            Complex[] input = { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };
            var copy = input.ToArray();

            FFT.Par(input);
            FFT.Seq(copy);

            for (int i = 0; i < input.Length; ++i)
            {
                if (!ApproxEqual(copy[i].Real, input[i].Real)) throw new Exception();
                if (!ApproxEqual(copy[i].Imaginary, input[i].Imaginary)) throw new Exception();
            }
        }
    }

    class Program
    {
        static void StartDebugging()
        {
            Campy.Utils.Options.Set("graph_trace");
            Campy.Utils.Options.Set("module_trace");
            Campy.Utils.Options.Set("name_trace");
            Campy.Utils.Options.Set("cfg_construction_trace");
            Campy.Utils.Options.Set("dot_graph");
            Campy.Utils.Options.Set("jit_trace");
            Campy.Utils.Options.Set("memory_trace");
            Campy.Utils.Options.Set("ptx_trace");
            Campy.Utils.Options.Set("state_computation_trace");
            Campy.Utils.Options.Set("continue_with_no_resolve");
            Campy.Utils.Options.Set("copy_trace");
            Campy.Utils.Options.Set("runtime_trace");
        }

        static void Main(string[] args)
        {
            StartDebugging();
            List<int> x = new List<int>();
            int n = 4;
            for (int i = 0; i < n; ++i) x.Add(0);
            Campy.Parallel.For(n, i => x[i] = i);
            Campy.Parallel.For(n, i => x[i] = x[i] * 2);
            for (int i = 0; i < n; ++i) System.Console.WriteLine(x[i]);
        }
    }
}
