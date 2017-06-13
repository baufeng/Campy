﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Campy.Graphs;

namespace Campy.GraphAlgorithms
{

    /// <summary>
    /// This class enumerates a topological sort of a graph. If the graph
    /// is not a DAG, then it will fail.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class TopologicalSort<T> : IEnumerable<T>
    {

        IGraph<T> graph;
        IEnumerable<T> Source;
        Dictionary<T, bool> Visited = new Dictionary<T, bool>();

        public TopologicalSort(IGraph<T> g, IEnumerable<T> s)
        {
            graph = g;
            Source = s;
            foreach (T v in graph.Vertices)
                Visited.Add(v, false);
        }

        public TopologicalSort(IGraph<T> g, T s)
        {
            graph = g;
            Source = new T[] {s};
            foreach (T v in graph.Vertices)
                Visited.Add(v, false);
        }

        /// Topological Sorting (Kahn's algorithm) 
        public IEnumerator<T> GetEnumerator()
        {
            if (Source != null && Source.Any())
            {
                HashSet<T> nodes = new HashSet<T>();
                foreach (T v in graph.Vertices) nodes.Add(v);
                HashSet<Tuple<T, T>> edges = new HashSet<Tuple<T, T>>();
                foreach (IEdge<T> e in graph.Edges) edges.Add(new Tuple<T, T>(e.From, e.To));
                
                // Set of all nodes with no incoming edges
                var S = new HashSet<T>(nodes.Where(n => edges.All(e => e.Item2.Equals(n) == false)));

                // while S is non-empty do
                while (S.Any())
                {

                    //  remove a node n from S
                    var n = S.First();
                    S.Remove(n);

                    // add n to tail of L
                    yield return n;

                    // for each node m with an edge e from n to m do
                    var look = edges.Where(e => e.Item1.Equals(n)).ToList();
                    foreach (var e in look)
                    {
                        var m = e.Item2;

                        // remove edge e from the graph
                        edges.Remove(e);

                        // if m has no other incoming edges then
                        if (edges.All(me => me.Item2.Equals(m) == false))
                        {
                            // insert m into S
                            S.Add(m);
                        }
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

