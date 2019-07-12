using System;
using System.Collections.Generic;

namespace Flatbush
{
    /// <summary>
    /// Static 2d spatial index implemented using packed Hilbert R-tree.
    /// </summary>
    public class SpatialIndex
    {
        private readonly int _numItems;
        private readonly int _nodeSize;
        private readonly List<int> _levelBounds;

        private readonly double[] _boxes;
        private readonly int[] _indices;
        private int _pos;

        /// <summary>
        /// Create a new static 2d spatial index.
        /// </summary>
        /// <param name="numItems">The fixed number of 2d boxes to be included in the index.</param>
        /// <param name="nodeSize">Size of the tree node, adjust to tune for particular use case performance.</param>
        public SpatialIndex(int numItems, int nodeSize = 16)
        {
            if (numItems <= 0)
            {
                throw new ArgumentException("numItems must be greater than zero", nameof(numItems));
            }

            _numItems = numItems;
            _nodeSize = Math.Min(Math.Max(nodeSize, 2), 65535);
            // calculate the total number of nodes in the R-tree to allocate space for
            // and the index of each tree level (used in search later)
            var n = numItems;
            var numNodes = n;
            _levelBounds = new List<int>();
            _levelBounds.Add(n * 4);
            do
            {
                n = (int)Math.Ceiling((double)n / _nodeSize);
                numNodes += n;
                _levelBounds.Add(numNodes * 4);
            } while (n != 1);

            _boxes = new double[numNodes * 4];
            _indices = new int[numNodes];
            _pos = 0;
            MinX = double.PositiveInfinity;
            MinY = double.PositiveInfinity;
            MaxX = double.NegativeInfinity;
            MaxY = double.NegativeInfinity;
        }

        /// <summary>
        /// Min x extent of all the boxes in this spatial index.
        /// </summary>
        public double MinX { get; private set; }
        /// <summary>
        /// Min y extent of all the boxes in this spatial index.
        /// </summary>
        public double MinY { get; private set; }
        /// <summary>
        /// Max x extent of all the boxes in this spatial index.
        /// </summary>
        public double MaxX { get; private set; }
        /// <summary>
        /// Max y extent of all the boxes in this spatial index.
        /// </summary>
        public double MaxY { get; private set; }

        /// <summary>
        /// Add a new 2d box to the spatial index, must not go over the static size given at time of construction.
        /// </summary>
        /// <param name="minX">Min x value of the box to be added.</param>
        /// <param name="minY">Min y value of the box to be added.</param>
        /// <param name="maxX">Max x value of the box to be added.</param>
        /// <param name="maxY">Max y value of the box to be added.</param>
        public void Add(double minX, double minY, double maxX, double maxY)
        {
            var index = _pos >> 2;
            _indices[index] = index;
            _boxes[_pos++] = minX;
            _boxes[_pos++] = minY;
            _boxes[_pos++] = maxX;
            _boxes[_pos++] = maxY;

            if (minX < this.MinX) this.MinX = minX;
            if (minY < this.MinY) this.MinY = minY;
            if (maxX > this.MaxX) this.MaxX = maxX;
            if (maxY > this.MaxY) this.MaxY = maxY;
        }

        /// <summary>
        /// Method to perform the indexing, to be called after adding all the boxes via <see cref="Add"/>.
        /// </summary>
        public void Finish()
        {
            if (_pos >> 2 != _numItems)
            {
                throw new InvalidOperationException($"Added {_pos >> 2} items when expected {_numItems}.");
            }

            var width = this.MaxX - this.MinX;
            var height = this.MaxY - this.MinY;
            var hilbertValues = new uint[_numItems];
            int pos = 0;

            // map item centers into Hilbert coordinate space and calculate Hilbert values
            for (int i = 0; i < _numItems; i++)
            {
                pos = 4 * i;
                var minX = _boxes[pos++];
                var minY = _boxes[pos++];
                var maxX = _boxes[pos++];
                var maxY = _boxes[pos++];

                const int n = 1 << 16;
                // hilbert max input value for x and y
                const int hilbertMax = n - 1;
                // mapping the x and y coordinates of the center of the box to values in the range [0 -> n - 1] such that
                // the min of the entire set of bounding boxes maps to 0 and the max of the entire set of bounding boxes maps to n - 1
                // our 2d space is x: [0 -> n-1] and y: [0 -> n-1], our 1d hilbert curve value space is d: [0 -> n^2 - 1]
                var x = (uint)Math.Floor(hilbertMax * ((minX + maxX) / 2 - this.MinX) / width);
                var y = (uint)Math.Floor(hilbertMax * ((minY + maxY) / 2 - this.MinY) / height);
                hilbertValues[i] = Hilbert(x, y);
            }

            // sort items by their Hilbert value (for packing later)
            Sort(hilbertValues, _boxes, _indices, 0, _numItems - 1);

            // generate nodes at each tree level, bottom-up
            pos = 0;
            for (var i = 0; i < _levelBounds.Count - 1; i++)
            {
                var end = this._levelBounds[i];

                // generate a parent node for each block of consecutive <nodeSize> nodes
                while (pos < end)
                {
                    var nodeMinX = double.PositiveInfinity;
                    var nodeMinY = double.PositiveInfinity;
                    var nodeMaxX = double.NegativeInfinity;
                    var nodeMaxY = double.NegativeInfinity;
                    var nodeIndex = pos;

                    // calculate bbox for the new node
                    for (var j = 0; j < _nodeSize && pos < end; j++)
                    {
                        var minX = _boxes[pos++];
                        var minY = _boxes[pos++];
                        var maxX = _boxes[pos++];
                        var maxY = _boxes[pos++];
                        if (minX < nodeMinX) nodeMinX = minX;
                        if (minY < nodeMinY) nodeMinY = minY;
                        if (maxX > nodeMaxX) nodeMaxX = maxX;
                        if (maxY > nodeMaxY) nodeMaxY = maxY;
                    }

                    // add the new node to the tree data
                    _indices[_pos >> 2] = nodeIndex;
                    _boxes[_pos++] = nodeMinX;
                    _boxes[_pos++] = nodeMinY;
                    _boxes[_pos++] = nodeMaxX;
                    _boxes[_pos++] = nodeMaxY;
                }
            }
        }

        /// <summary>
        /// Returns a list of indices to boxes that intersect or overlap the bounding box given, <see cref="Finish"/> must be called before querying.
        /// </summary>
        /// <param name="minX">Min x value of the bounding box.</param>
        /// <param name="minY">Min y value of the bounding box.</param>
        /// <param name="maxX">Max x value of the bounding box.</param>
        /// <param name="maxY">Max y value of the bounding box.</param>
        /// <param name="filter">Optional filter function, if not null then only indices for which the filter function returns true will be included.</param>
        /// <returns>List of indices that intersect or overlap with the bounding box given.</returns>
        public IReadOnlyList<int> Query(double minX, double minY, double maxX, double maxY, Func<int, bool> filter = null)
        {
            if (_pos != _boxes.Length)
            {
                throw new InvalidOperationException("Data not yet indexed - call Finish().");
            }

            var nodeIndex = _boxes.Length - 4;
            var level = _levelBounds.Count - 1;

            // stack for traversing nodes
            var stack = new Stack<int>();

            var results = new List<int>();

            var done = false;

            while (!done)
            {
                // find the end index of the node
                var end = Math.Min(nodeIndex + _nodeSize * 4, _levelBounds[level]);

                // search through child nodes
                for (var pos = nodeIndex; pos < end; pos += 4)
                {
                    var index = this._indices[pos >> 2];

                    // check if node bbox intersects with query bbox
                    if (maxX < _boxes[pos]) continue; // maxX < nodeMinX
                    if (maxY < _boxes[pos + 1]) continue; // maxY < nodeMinY
                    if (minX > _boxes[pos + 2]) continue; // minX > nodeMaxX
                    if (minY > _boxes[pos + 3]) continue; // minY > nodeMaxY

                    if (nodeIndex < _numItems * 4)
                    {
                        if (filter == null || filter(index))
                        {
                            results.Add(index); // leaf item
                        }

                    }
                    else
                    {
                        // push node index and level for further traversal
                        stack.Push(index); 
                        stack.Push(level - 1);
                    }
                }

                if (stack.Count > 1)
                {
                    level = stack.Pop();
                    nodeIndex = stack.Pop();
                }
                else
                {
                    done = true;
                }
            }

            return results;
        }

        /// <summary>
        /// Invokes a function on each of the indices of boxes that intersect or overlap with the bounding box given, <see cref="Finish"/> must be called before querying.
        /// </summary>
        /// <param name="minX">Min x value of the bounding box.</param>
        /// <param name="minY">Min y value of the bounding box.</param>
        /// <param name="maxX">Max x value of the bounding box.</param>
        /// <param name="maxY">Max y value of the bounding box.</param>
        /// <param name="visitor">The function to visit each of the result indices, if false is returned no more results will be visited.</param>
        public void VisitQuery(double minX, double minY, double maxX, double maxY, Func<int, bool> visitor)
        {
            if (_pos != _boxes.Length)
            {
                throw new InvalidOperationException("Data not yet indexed - call Finish().");
            }

            if (visitor == null)
            {
                throw new ArgumentNullException(nameof(visitor));
            }

            var nodeIndex = _boxes.Length - 4;
            var level = _levelBounds.Count - 1;

            // stack for traversing nodes
            var stack = new Stack<int>();

            var done = false;

            while (!done)
            {
                // find the end index of the node
                var end = Math.Min(nodeIndex + _nodeSize * 4, _levelBounds[level]);

                // search through child nodes
                for (var pos = nodeIndex; pos < end; pos += 4)
                {
                    var index = this._indices[pos >> 2];

                    // check if node bbox intersects with query bbox
                    if (maxX < _boxes[pos]) continue; // maxX < nodeMinX
                    if (maxY < _boxes[pos + 1]) continue; // maxY < nodeMinY
                    if (minX > _boxes[pos + 2]) continue; // minX > nodeMaxX
                    if (minY > _boxes[pos + 3]) continue; // minY > nodeMaxY

                    if (nodeIndex < _numItems * 4)
                    {
                        done = !visitor(index);
                        if (done)
                        {
                            break;
                        }
                    }
                    else
                    {
                        // push node index and level for further traversal
                        stack.Push(index); 
                        stack.Push(level - 1);
                    }
                }

                if (stack.Count > 1)
                {
                    level = stack.Pop();
                    nodeIndex = stack.Pop();
                }
                else
                {
                    done = true;
                }
            }
        }

        // custom quicksort that sorts bbox data alongside the hilbert values
        private static void Sort(uint[] values, double[] boxes, int[] indices, int left, int right)
        {
            if (left >= right) return;

            var pivot = values[(left + right) >> 1];
            var i = left - 1;
            var j = right + 1;

            while (true)
            {
                do i++; while (values[i] < pivot);
                do j--; while (values[j] > pivot);
                if (i >= j) break;
                Swap(values, boxes, indices, i, j);
            }

            Sort(values, boxes, indices, left, j);
            Sort(values, boxes, indices, j + 1, right);
        }

        // swap two values and two corresponding boxes
        private static void Swap(uint[] values, double[] boxes, int[] indices, int i, int j)
        {
            var temp = values[i];
            values[i] = values[j];
            values[j] = temp;

            var k = 4 * i;
            var m = 4 * j;

            var a = boxes[k];
            var b = boxes[k + 1];
            var c = boxes[k + 2];
            var d = boxes[k + 3];
            boxes[k] = boxes[m];
            boxes[k + 1] = boxes[m + 1];
            boxes[k + 2] = boxes[m + 2];
            boxes[k + 3] = boxes[m + 3];
            boxes[m] = a;
            boxes[m + 1] = b;
            boxes[m + 2] = c;
            boxes[m + 3] = d;

            var e = indices[i];
            indices[i] = indices[j];
            indices[j] = e;
        }

        // Fast Hilbert curve algorithm by http://threadlocalmutex.com/
        // Ported from C++ https://github.com/rawrunprotected/hilbert_curves (public domain)
        private static uint Hilbert(uint x, uint y)
        {

            var a = x ^ y;
            var b = 0xFFFF ^ a;
            var c = 0xFFFF ^ (x | y);
            var d = x & (y ^ 0xFFFF);

            var A = a | (b >> 1);
            var B = (a >> 1) ^ a;
            var C = ((c >> 1) ^ (b & (d >> 1))) ^ c;
            var D = ((a & (c >> 1)) ^ (d >> 1)) ^ d;

            a = A; b = B; c = C; d = D;
            A = ((a & (a >> 2)) ^ (b & (b >> 2)));
            B = ((a & (b >> 2)) ^ (b & ((a ^ b) >> 2)));
            C ^= ((a & (c >> 2)) ^ (b & (d >> 2)));
            D ^= ((b & (c >> 2)) ^ ((a ^ b) & (d >> 2)));

            a = A; b = B; c = C; d = D;
            A = ((a & (a >> 4)) ^ (b & (b >> 4)));
            B = ((a & (b >> 4)) ^ (b & ((a ^ b) >> 4)));
            C ^= ((a & (c >> 4)) ^ (b & (d >> 4)));
            D ^= ((b & (c >> 4)) ^ ((a ^ b) & (d >> 4)));

            a = A; b = B; c = C; d = D;
            C ^= ((a & (c >> 8)) ^ (b & (d >> 8)));
            D ^= ((b & (c >> 8)) ^ ((a ^ b) & (d >> 8)));

            a = C ^ (C >> 1);
            b = D ^ (D >> 1);

            var i0 = x ^ y;
            var i1 = b | (0xFFFF ^ (i0 | a));

            i0 = (i0 | (i0 << 8)) & 0x00FF00FF;
            i0 = (i0 | (i0 << 4)) & 0x0F0F0F0F;
            i0 = (i0 | (i0 << 2)) & 0x33333333;
            i0 = (i0 | (i0 << 1)) & 0x55555555;

            i1 = (i1 | (i1 << 8)) & 0x00FF00FF;
            i1 = (i1 | (i1 << 4)) & 0x0F0F0F0F;
            i1 = (i1 | (i1 << 2)) & 0x33333333;
            i1 = (i1 | (i1 << 1)) & 0x55555555;

            return (i1 << 1) | i0;
        }
    }
}
