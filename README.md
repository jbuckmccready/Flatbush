# Flatbush
C# port of the fast static 2d spatial index Flatbush in javascript (https://github.com/mourner/flatbush/).

Implementation is a packed hilbert R-tree (https://en.wikipedia.org/wiki/Hilbert_R-tree#Packed_Hilbert_R-trees), the library is built purely for speed. All queries return indices to the boxes that were added at time of build up. Entries cannot be added or removed after calling the Finish method (static structure).

Currently does not support k nearest neighbors query (which is implemented in the javascript version). I added an additional method to use a visiting function which allows for stopping the query early and does not require having to return the index results.

Currently only supports signed int32 indexes and doubles.

Ideas and contributions are welcome. Tests and benchmarks need to be added.

# Documentation
See SpatialIndex class methods and example code below.

# Example Code
Build the index up
```csharp
using Flatbush;
// Create a new spatial index to hold 5 boxes
var spatialIndex = new SpatialIndex(5);
spatialIndex.Add(-1.1, -1.1, 1.1, 1.1); // index 0
spatialIndex.Add(-5.2, -5.3, 5.4, 5.5); // index 1
spatialIndex.Add(-5.2, -5.3, 1.4, 1.5); // index 2
spatialIndex.Add(1.7, 1.6, 2.2, 5.0); // index 3
spatialIndex.Add(9.9, 10.1, 20.2, 20.9); // index 4
// Done adding items, build the index
spatialIndex.Finish();
```
Query the index
```csharp
// Query all boxes that overlap or intersect a box on the point 0,0
var results = spatialIndex.Query(0, 0, 0, 0);
// Prints the set [0, 1, 2] (order not defined)
foreach (var i in results)
{
    Console.Write(i);
}
```
Utilizing a filter function
```csharp
// Same as previous query but index 1 will not be included
results = spatialIndex.Query(0, 0, 0, 0, i => i != 1);
foreach (var i in results)
{
    Console.Write(i);
}
```
Utilizing a visiting function
```csharp
// Visit all query results with a function rather than having a list of indices returned
spatialIndex.VisitQuery(12, 12, 15, 15, i => { Console.Write(i); return true; });
```
Utilizing a visiting function that stops the query early
```csharp
// Visit query results until index equals 1
spatialIndex.VisitQuery(0, 0, 0, 0, i => { Console.Write(i); return i == 1; });
```
## Additional References
- Awesome work done for hilbert curve functions: https://github.com/rawrunprotected/hilbert_curves
- RBush for non-static spatial index: https://github.com/mourner/rbush and https://github.com/viceroypenguin/RBush
