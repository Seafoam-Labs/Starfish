using Gtk;
using Cairo;
using Graphene;

namespace Starfish.Windows;

public partial class GskGraphWidget : DrawingArea
{
    private string _rootPackage = string.Empty;
    private Dictionary<string, List<string>> _dependencyMap = new();
    private readonly Dictionary<string, Point> _positions = new();
    private readonly Dictionary<string, Point> _velocities = new();
    private Dictionary<string, int> _levels = new();
    private readonly HashSet<string> _foregroundNodes = [];
    private readonly Lock _lock = new();

    private double _zoom = 1.0;
    private double _panX, _panY;

    private uint _tickId;
    private bool _isSimulating;

#pragma warning disable GirCore1007
    //Looks like issue on github about this 
    public GskGraphWidget()
    {
        Setup();
    }
#pragma warning restore GirCore1007

    private void Setup()
    {
        SetDrawFunc(DrawInternal);
      
        _tickId = AddTickCallback((_, _) =>
        {
            if (!_isSimulating) return true;
            UpdateSimulation();
            QueueDraw();

            return true;
        });
    }

    public void ToggleForeground(string packageName)
    {
        lock (_lock)
        {
            if (_foregroundNodes.Contains(packageName))
            {
                _foregroundNodes.Clear();
            }
            else
            {
                _foregroundNodes.Clear();
                _foregroundNodes.Add(packageName);
            }
        }

        QueueDraw();
    }

    public void ClearSelection()
    {
        lock (_lock)
        {
            _foregroundNodes.Clear();
        }
        QueueDraw();
    }

    public void UpdateData(string rootPackage, Dictionary<string, List<string>> dependencyMap)
    {
        lock (_lock)
        {
            _rootPackage = rootPackage;
            _dependencyMap = dependencyMap;
            CalculateInitialLayout();
            
            //150 seems to be a good number for good looking graphs. While not taking too long.
            RunSimulationStepForceAtlas2(150);
            _isSimulating = false;
        }

        QueueDraw();
    }

    public void SetTransform(double zoom, double panX, double panY)
    {
        _zoom = zoom;
        _panX = panX;
        _panY = panY;
        QueueDraw();
    }

    public string? GetPackageAt(double x, double y)
    {
        var w = GetAllocatedWidth();
        var h = GetAllocatedHeight();
        var gx = (x - w / 2.0 - _panX) / _zoom;
        var gy = (y - h / 2.0 - _panY) / _zoom;

        const double half = 60 / 2.0;
        lock (_lock)
        {
            foreach (var (name, pos) in _positions)
            {
                if (!(gx >= pos.X - half) || !(gx <= pos.X + half) ||
                    !(gy >= pos.Y - half) || !(gy <= pos.Y + half)) continue;
                return name;
            }
        }

        return null;
    }

    private void CalculateInitialLayout()
    {
        if (string.IsNullOrEmpty(_rootPackage)) return;

        _levels = new Dictionary<string, int> { [_rootPackage] = 0 };
        var childrenMap = new Dictionary<string, List<string>>();
        var queue = new Queue<string>();
        queue.Enqueue(_rootPackage);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!_dependencyMap.TryGetValue(current, out var deps)) continue;

            childrenMap[current] = [];
            foreach (var dep in deps.Where(dep => !_levels.ContainsKey(dep)))
            {
                _levels[dep] = _levels[current] + 1;
                childrenMap[current].Add(dep);
                queue.Enqueue(dep);
            }
        }

        _positions.Clear();
        _velocities.Clear();
        
        var leafCounts = new Dictionary<string, int>();
        CalculateLeafCounts(_rootPackage, childrenMap, leafCounts);

        // Radial Layered Layout Calculation
        const float levelRadius = 500f;
        _positions[_rootPackage] = new Point { X = 0, Y = 0 };
        _velocities[_rootPackage] = new Point { X = 0, Y = 0 };

        PositionNodesRadial(_rootPackage, 0, 2 * (float)Math.PI, childrenMap, leafCounts, levelRadius);

        _isSimulating = false;
    }

    private static int CalculateLeafCounts(string node, Dictionary<string, List<string>> childrenMap,
        Dictionary<string, int> leafCounts)
    {
        if (!childrenMap.TryGetValue(node, out var children) || children.Count == 0)
        {
            leafCounts[node] = 1;
            return 1;
        }

        var count = children.Sum(child => CalculateLeafCounts(child, childrenMap, leafCounts));

        leafCounts[node] = count;
        return count;
    }

    private void PositionNodesRadial(string parent, float minAngle, float maxAngle,
        Dictionary<string, List<string>> childrenMap, Dictionary<string, int> leafCounts, float radius)
    {
        if (!childrenMap.TryGetValue(parent, out var children) || children.Count == 0) return;

        var totalLeaves = leafCounts[parent];
        var currentAngle = minAngle;

        // Add a small angular buffer between siblings (e.g., 2 degrees)
        const float angularBuffer = 0.035f;
        var availableAngle = maxAngle - minAngle;
        var totalBuffer = angularBuffer * (children.Count - 1);

        // If the buffer is too large for the available space, reduce it
        var effectiveBuffer = (totalBuffer > availableAngle * 0.5f)
            ? (availableAngle * 0.5f / Math.Max(1, children.Count - 1))
            : angularBuffer;

        foreach (var child in children)
        {
            var childLeaves = leafCounts[child];

            // Calculate span proportionally but leave room for buffers
            var angleSpan = (availableAngle - (children.Count - 1) * effectiveBuffer) *
                            (childLeaves / (float)totalLeaves);
            var angle = currentAngle + angleSpan / 2f;

            var x = (float)(Math.Cos(angle) * radius);
            var y = (float)(Math.Sin(angle) * radius);

            _positions[child] = new Point { X = x, Y = y };
            _velocities[child] = new Point { X = 0, Y = 0 };

            PositionNodesRadial(child, currentAngle, currentAngle + angleSpan, childrenMap, leafCounts, radius + 500f);

            currentAngle += angleSpan + effectiveBuffer;
        }
    }

    private void UpdateSimulation()
    {
        lock (_lock)
        {
            if (_positions.Count == 0) return;
            if (!RunSimulationStepForceAtlas2(10))
            {
                _isSimulating = false;
            }
        }
    }
    
    private bool RunSimulationStepForceAtlas2(int iterations)
    {
        if (_positions.Count == 0) return false;

        const float kR = 600f; // Repulsion constant
        const float kA = 0.5f; // Attraction constant
        const float kG = 0.2f; // Gravity constant
        const float damping = 0.8f;
        const float timeStep = 0.2f;
        const float maxForce = 50f;
        var random = new Random();

      
        var nodes = _positions.Keys.ToList();
        var nodeToIndex = new Dictionary<string, int>();
        for (var i = 0; i < nodes.Count; i++) nodeToIndex[nodes[i]] = i;

        var rootIndex = -1;
        if (!string.IsNullOrEmpty(_rootPackage)) nodeToIndex.TryGetValue(_rootPackage, out rootIndex);

        // 2. Pre-calculate degrees and edges using indices
        var degrees = new int[nodes.Count];
        var adjacencyList = new List<int>[nodes.Count];
        for (var i = 0; i < nodes.Count; i++) adjacencyList[i] = [];

        foreach (var (parent, deps) in _dependencyMap)
        {
            if (!nodeToIndex.TryGetValue(parent, out var u)) continue;
            foreach (var dep in deps)
            {
                if (!nodeToIndex.TryGetValue(dep, out var v)) continue;
                adjacencyList[u].Add(v);
                degrees[u]++;
                degrees[v]++;
            }
        }

        // 3. Move positions and velocities to flat arrays for fast access
        var px = new float[nodes.Count];
        var py = new float[nodes.Count];
        var vx = new float[nodes.Count];
        var vy = new float[nodes.Count];

        for (var i = 0; i < nodes.Count; i++)
        {
            var p = _positions[nodes[i]];
            var v = _velocities[nodes[i]];
            px[i] = p.X;
            py[i] = p.Y;
            vx[i] = v.X;
            vy[i] = v.Y;
        }

        // 4. Pre-calculate foreground indices
        var isForeground = new bool[nodes.Count];
        var  hasForeground = _foregroundNodes.Count > 0;
        foreach (var fgNode in _foregroundNodes)
        {
            if (nodeToIndex.TryGetValue(fgNode, out var idx)) isForeground[idx] = true;
        }

        // 5. Main Simulation Loop
        for (var it = 0; it < iterations; it++)
        {
            var fx = new float[nodes.Count];
            var fy = new float[nodes.Count];

            // Repulsion 
            for (var i = 0; i < nodes.Count; i++)
            {
                var xA = px[i];
                var yA = py[i];
                var degA = degrees[i];

                for (var j = i + 1; j < nodes.Count; j++)
                {
                    var dx = xA - px[j];
                    var dy = yA - py[j];
                    var distSq = dx * dx + dy * dy;

                    if (distSq < 1.0f)
                    {
                        dx = (float)(random.NextDouble() - 0.5);
                        dy = (float)(random.NextDouble() - 0.5);
                        distSq = dx * dx + dy * dy + 0.1f;
                    }

                    var dist = (float)Math.Sqrt(distSq);
                    var force = Math.Min(kR * (degA + 1) * (degrees[j] + 1) / dist, maxForce * 2);
                    var dfx = (dx / dist) * force;
                    var dfy = (dy / dist) * force;

                    fx[i] += dfx;
                    fy[i] += dfy;
                    fx[j] -= dfx;
                    fy[j] -= dfy;
                }
            }

            // Attraction
            for (var u = 0; u < nodes.Count; u++)
            {
                var xU = px[u];
                var yU = py[u];
                foreach (var v in adjacencyList[u])
                {
                    var dx = px[v] - xU;
                    var dy = py[v] - yU;
                    var distSq = dx * dx + dy * dy + 0.01f;
                    var dist = (float)Math.Sqrt(distSq);

                    var force = kA * dist;
                    var dfx = Math.Clamp((dx / dist) * force, -maxForce, maxForce);
                    var dfy = Math.Clamp((dy / dist) * force, -maxForce, maxForce);

                    fx[u] += dfx;
                    fy[u] += dfy;
                    fx[v] -= dfx;
                    fy[v] -= dfy;
                }
            }

            // Gravity
            for (var i = 0; i < nodes.Count; i++)
            {
                var x = px[i];
                var y = py[i];
                var distSq = x * x + y * y + 0.01f;
                var dist = (float)Math.Sqrt(distSq);

                var force = kG * dist * (degrees[i] + 1);
                fx[i] -= (x / dist) * force;
                fy[i] -= (y / dist) * force;
            }

            // Update Positions & Velocities
            float totalVelocity = 0;
            for (var i = 0; i < nodes.Count; i++)
            {
                // Root is anchored if no foreground selection
                if (i == rootIndex && !hasForeground) continue;

                vx[i] = (vx[i] + fx[i] * timeStep) * damping;
                vy[i] = (vy[i] + fy[i] * timeStep) * damping;

                totalVelocity += (float)Math.Sqrt(vx[i] * vx[i] + vy[i] * vy[i]);

                px[i] += vx[i] * timeStep;
                py[i] += vy[i] * timeStep;
            }

            if (totalVelocity < 0.005f * nodes.Count)
            {
                break;
            }
        }

        // Sync results back to dictionaries
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            _positions[node] = new Point { X = px[i], Y = py[i] };
            _velocities[node] = new Point { X = vx[i], Y = vy[i] };
        }

        return true;
    }

    private void DrawInternal(DrawingArea area, Context cr, int w, int h)
    {
        if (string.IsNullOrEmpty(_rootPackage)) return;

        cr.Translate(w / 2.0 + _panX, h / 2.0 + _panY);
        cr.Scale(_zoom, _zoom);

        DrawEdges(cr);
        DrawNodes(cr);
    }

    private void DrawEdges(Context cr)
    {
        lock (_lock)
        {
            var nodes = _positions.Keys.ToList();
            var nodeToIndex = new Dictionary<string, int>();
            for (var i = 0; i < nodes.Count; i++) nodeToIndex[nodes[i]] = i;

            var px = new float[nodes.Count];
            var py = new float[nodes.Count];
            for (var i = 0; i < nodes.Count; i++)
            {
                var p = _positions[nodes[i]];
                px[i] = p.X;
                py[i] = p.Y;
            }

            // Draw background edges
            if (_foregroundNodes.Count > 0)
                cr.SetSourceRgb(0.2, 0.2, 0.2); // Dimmer edges if something is selected
            else
                cr.SetSourceRgb(0.5, 0.5, 0.5);
            
            cr.LineWidth = 1.0 / _zoom;

            foreach (var (package, deps) in _dependencyMap)
            {
                if (!nodeToIndex.TryGetValue(package, out var u)) continue;
                var isUForeground = _foregroundNodes.Contains(package);

                foreach (var dep in deps)
                {
                    if (!nodeToIndex.TryGetValue(dep, out var v)) continue;
                    if (isUForeground || _foregroundNodes.Contains(dep)) continue;

                    cr.MoveTo(px[u], py[u]);
                    cr.LineTo(px[v], py[v]);
                    cr.Stroke();
                }
            }

            // Draw foreground edges
            cr.SetSourceRgb(1.0, 1.0, 0.0);
            cr.LineWidth = 2.5 / _zoom;

            foreach (var (package, deps) in _dependencyMap)
            {
                if (!nodeToIndex.TryGetValue(package, out var u)) continue;
                var isUForeground = _foregroundNodes.Contains(package);

                foreach (var dep in deps)
                {
                    if (!nodeToIndex.TryGetValue(dep, out var v)) continue;
                    if (!isUForeground && !_foregroundNodes.Contains(dep)) continue;
                    cr.MoveTo(px[u], py[u]);
                    cr.LineTo(px[v], py[v]);
                    cr.Stroke();
                }
            }
        }
    }

    private void DrawNodes(Context cr)
    {
        lock (_lock)
        {
            const float nodeSize = 60;
            (double R, double G, double B)[] levelColors =
            [
                (0.85, 0.35, 0.35),
                (0.30, 0.55, 0.85),
                (0.25, 0.75, 0.50),
                (0.80, 0.60, 0.20),
                (0.85, 0.35, 0.35),
                (0.30, 0.55, 0.85),
                (0.25, 0.75, 0.50),
                (0.80, 0.60, 0.20),
                (0.85, 0.35, 0.35),
                (0.30, 0.55, 0.85),
                (0.25, 0.75, 0.50),
                (0.80, 0.60, 0.20),
            ];

            // Get the selected node and its neighbors
            var selectedNode = _foregroundNodes.FirstOrDefault();
            HashSet<string> highlightedNodes = [];
            if (selectedNode != null)
            {
                highlightedNodes.Add(selectedNode);
                // Dependencies (children)
                if (_dependencyMap.TryGetValue(selectedNode, out var deps))
                {
                    foreach (var dep in deps) highlightedNodes.Add(dep);
                }
                // Dependants (parents)
                foreach (var (parent, children) in _dependencyMap)
                {
                    if (children.Contains(selectedNode)) highlightedNodes.Add(parent);
                }
            }

            var shouldDim = selectedNode != null;
            foreach (var (name, pos) in _positions)
            {
                if (highlightedNodes.Contains(name)) continue;
                var level = _levels.GetValueOrDefault(name, 0);
                var color = levelColors[Math.Min(level, levelColors.Length - 1)];
                
                // Dim only when a node is selected
                var drawColor = shouldDim 
                    ? (color.R * 0.3, color.G * 0.3, color.B * 0.3) 
                    : color;
                
                DrawNode(cr, name, pos, nodeSize, drawColor, false, false, shouldDim);
            }

            foreach (var name in highlightedNodes)
            {
                if (!_positions.TryGetValue(name, out var pos)) continue;
                var level = _levels.GetValueOrDefault(name, 0);
                var color = levelColors[Math.Min(level, levelColors.Length - 1)];
                var isSelected = name == selectedNode;
                DrawNode(cr, name, pos, nodeSize, color, true, isSelected, false);
            }
        }
    }

    private void DrawNode(Context cr, string name, Point pos, float nodeSize, (double R, double G, double B) color,
        bool isHighlighted, bool isSelected, bool isDimmed)
    {
        var half = nodeSize / 2;
        cr.SetSourceRgb(color.R, color.G, color.B);
        cr.Rectangle(pos.X - half, pos.Y - half, nodeSize, nodeSize);
        cr.FillPreserve();

        if (isSelected)
        {
            cr.SetSourceRgb(1, 1, 0);
            cr.LineWidth = 3.0 / _zoom;
        }
        else if (isHighlighted)
        {
            cr.SetSourceRgb(1, 0.5, 0); 
            cr.LineWidth = 2.0 / _zoom;
        }
        else
        {
            if (isDimmed)
                cr.SetSourceRgb(0.3, 0.3, 0.3);
            else
                cr.SetSourceRgb(1, 1, 1); 

            cr.LineWidth = 1.0 / _zoom;
        }
        cr.Stroke();
        
        if (isDimmed)
            cr.SetSourceRgb(0.5, 0.5, 0.5);
        else
            cr.SetSourceRgb(1, 1, 1);

        cr.SetFontSize(10 / _zoom);
        cr.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        cr.TextExtents(name, out var te);
        cr.MoveTo(pos.X - te.Width / 2, pos.Y + half + te.Height + 5 / _zoom);
        cr.ShowText(name);
    }

    public override void Dispose()
    {
        RemoveTickCallback(_tickId);
        base.Dispose();
    }
}