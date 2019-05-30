# Flatbush
C# port of the fast static 2d spatial index Flatbush in javascript, check it out [here](https://github.com/mourner/flatbush/).

Implementation is a [packed Hilbert R-tree](https://en.wikipedia.org/wiki/Hilbert_R-tree#Packed_Hilbert_R-trees).
- Very memory efficient and fast (tree nodes are packed into an array)
- Static size (no adding or removing items after indexing)
- Supports 2d axis aligned bounding boxes (point queries are done using a box with no height or width)

This structure is useful for computational geometry algorithms, e.g. finding all intersections between two arbitrary curves (mapping segments to bounding boxes), hit testing, 2d bounding box selections, etc.

Currently does not support k nearest neighbors query (which is implemented in the javascript version utilizing a priority queue). I added an additional method to use a visiting function which allows for stopping the query early and does not require having to return the index results.

Currently only supports signed int32 indexes and doubles for bounding box extents.

Ideas and contributions are welcome. Tests and benchmarks need to be added.

# Nuget Package
https://www.nuget.org/packages/Flatbush/
```
Install-Package Flatbush
```

# Documentation
See SpatialIndex class methods and example code below.

# Example Code
### Build the spatial index up
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
### Query the spatial index
```csharp
// Query all boxes (results are integer indices) that overlap a point at 0,0
var results = spatialIndex.Query(0, 0, 0, 0);
// Prints the set [0, 1, 2] (order not defined)
foreach (var i in results)
{
    Console.Write(i);
}
```
### Utilizing a filter function
```csharp
// Same as previous query (point at 0,0) but index 1 will not be included
results = spatialIndex.Query(0, 0, 0, 0, i => i != 1);
foreach (var i in results)
{
    Console.Write(i);
}
```
### Utilizing a visiting function
```csharp
// Visit all query results with a function rather than having a list of indices returned
// Querying results that intersect/overlap with the bounding box defined by
// (MinX = 12, MinY = 12, MaxX = 15, MaxY = 15)
spatialIndex.VisitQuery(12, 12, 15, 15, i => 
    { 
        Console.Write(i);
        // We return true to visit all the results (stops query early when return is false)
        return true; 
    });
```
### Utilizing a visiting function that stops the query early
```csharp
int visited = 0;
spatialIndex.VisitQuery(0, 0, 0, 0, i => 
    {
        Console.Write(i);
        visited += 1;
        // Visit only the first two results
        return visited < 2; 
    });
```
# Additional References
- Original source code for javascript: https://github.com/mourner/flatbush/
- Awesome work done for hilbert curve functions: https://github.com/rawrunprotected/hilbert_curves
- RBush for non-static spatial index: https://github.com/mourner/rbush and https://github.com/viceroypenguin/RBush
