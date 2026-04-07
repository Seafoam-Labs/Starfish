using Gtk;
using Cairo;
using Graphene;
using Silk.NET.OpenGL;
using Starfish.Constants;
using Starfish.References;

namespace Starfish.Windows;

public class GskGraphWidget : GLArea
{
    private string _rootPackage = string.Empty;
    private Dictionary<string, List<string>> _dependencyMap = new();
    private readonly Dictionary<string, Point> _positions = new();
    private readonly Dictionary<string, Point> _velocities = new();
    private Dictionary<string, int> _levels = new();
    private readonly HashSet<string> _foregroundNodes = [];
    private string? _hoverNode;
    private readonly Lock _lock = new();

    private double _zoom = 1.0;
    private double _panX, _panY;

    private GL? _gl;
    private uint _nodeVao, _nodeVbo, _instanceVbo;
    private uint _edgeVao, _edgeVbo, _edgeEbo;
    private uint _nodeShader, _edgeShader;
    private uint _nodeShaderPerf, _edgeShaderPerf;
    private bool _usePerformanceShaders;
    private bool _lockHover;
    private DrawingArea? _labelOverlay;
    private readonly DateTime _startTime = DateTime.UtcNow;

    private float[] _nodeInstanceData = new float[1024 * 7];
    private float[] _edgeVertexData = new float[4096 * 8];
    private uint[] _edgeIndexData = new uint[4096 * 2];

    private readonly Dictionary<string, float> _nodeScales = new();

    public bool UsePerformanceShaders
    {
        get => _usePerformanceShaders;
        set
        {
            if (_usePerformanceShaders == value) return;
            _usePerformanceShaders = value;
            QueueDraw();
        }
    }

    public bool LockHover
    {
        get => _lockHover;
        set
        {
            if (_lockHover == value) return;
            _lockHover = value;
            QueueDraw();
        }
    }

#pragma warning disable GirCore1007
    public GskGraphWidget()
    {
        SetRequiredVersion(3, 0);
        HasDepthBuffer = false;
        OnRealize += OnRealize_Handler;
        OnUnrealize += OnUnrealize_Handler;
        OnRender += OnRender_Handler;
    }
#pragma warning restore GirCore1007

    private void OnRealize_Handler(Widget sender, EventArgs e)
    {
        MakeCurrent();
        try
        {
            _gl = GlLoader.GetGl();
            Console.Error.WriteLine("[DEBUG_LOG] OpenGL API loaded successfully.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DEBUG_LOG] Failed to load OpenGL API: {ex.Message}");
            return;
        }

        var renderer = _gl.GetStringS(StringName.Renderer);
        var version = _gl.GetStringS(StringName.Version);
        Console.Error.WriteLine($"[DEBUG_LOG] GL Renderer: {renderer}");
        Console.Error.WriteLine($"[DEBUG_LOG] GL Version: {version}");

        _gl.Enable(EnableCap.Multisample);
        _gl.Enable(EnableCap.LineSmooth);
        _gl.Hint(HintTarget.LineSmoothHint, HintMode.Fastest);

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);

        SetupShaders();
        SetupBuffers();
    }

    private bool OnRender_Handler(GLArea sender, RenderSignalArgs args)
    {
        if (_gl is null) return false;

        var w = GetAllocatedWidth();
        var h = GetAllocatedHeight();

        _gl.Viewport(0, 0, (uint)w, (uint)h);
        _gl.ClearColor(0.15f, 0.15f, 0.15f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        var time = (float)(DateTime.UtcNow - _startTime).TotalSeconds;

        AnimateScales();
        DrawEdgesGl(w, h, time);
        DrawNodesGl(w, h);

        if (!ScalesAreAnimating()) return true;
        QueueDraw();
        _labelOverlay?.QueueDraw();

        return true;
    }

    private void OnUnrealize_Handler(Widget sender, EventArgs e)
    {
        MakeCurrent();
        if (_gl == null) return;
        _gl.DeleteVertexArray(_nodeVao);
        _gl.DeleteVertexArray(_edgeVao);
        _gl.DeleteBuffer(_nodeVbo);
        _gl.DeleteBuffer(_instanceVbo);
        _gl.DeleteBuffer(_edgeVbo);
        _gl.DeleteProgram(_nodeShader);
        _gl.DeleteProgram(_edgeShader);
        _gl.DeleteProgram(_nodeShaderPerf);
        _gl.DeleteProgram(_edgeShaderPerf);
    }

    public void SetLabelOverlay(DrawingArea overlay)
    {
        _labelOverlay = overlay;
    }


    private void SetupShaders()
    {
        _nodeShader = CreateShaderProgram(ShaderConstants.NodeVert, ShaderConstants.NodeFrag);
        _edgeShader = CreateShaderProgram(ShaderConstants.EdgeVert, ShaderConstants.EdgeFrag);
        _nodeShaderPerf = CreateShaderProgram(ShaderConstants.NodeVert, ShaderConstants.NodeFragPerformance);
        _edgeShaderPerf = CreateShaderProgram(ShaderConstants.EdgeVert, ShaderConstants.EdgeFragPerformance);
    }

    private uint CreateShaderProgram(string vertSrc, string fragSrc)
    {
        var gl = _gl!;

        var vert = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vert, vertSrc);
        gl.CompileShader(vert);
        gl.GetShader(vert, ShaderParameterName.CompileStatus, out var vOk);
        if (vOk == 0) Console.Error.WriteLine($"[DEBUG_LOG] Vert error: {gl.GetShaderInfoLog(vert)}");

        var frag = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(frag, fragSrc);
        gl.CompileShader(frag);
        gl.GetShader(frag, ShaderParameterName.CompileStatus, out var fOk);
        if (fOk == 0) Console.Error.WriteLine($"[DEBUG_LOG] Frag error: {gl.GetShaderInfoLog(frag)}");

        var prog = gl.CreateProgram();
        gl.AttachShader(prog, vert);
        gl.AttachShader(prog, frag);
        gl.LinkProgram(prog);
        gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out var lOk);
        if (lOk == 0) Console.Error.WriteLine($"[DEBUG_LOG] Link error: {gl.GetProgramInfoLog(prog)}");

        gl.DeleteShader(vert);
        gl.DeleteShader(frag);

        return prog;
    }

    private unsafe void SetupBuffers()
    {
        var gl = _gl!;

        float[] quad =
        [
            -1f, -1f,
            1f, -1f,
            1f, 1f,
            -1f, -1f,
            1f, 1f,
            -1f, 1f,
        ];

        _nodeVao = gl.GenVertexArray();
        gl.BindVertexArray(_nodeVao);

        // Quad vertices (loc 0)
        _nodeVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _nodeVbo);
        fixed (float* p = quad)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * 4),
                p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 8, (void*)0);

        // Layout per instance: x, y, radius, r, g, b, glow = 7 floats
        _instanceVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, 0, null, BufferUsageARB.DynamicDraw);

        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 28, (void*)0); // pos
        gl.VertexAttribDivisor(1, 1);

        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 28, (void*)8); // radius
        gl.VertexAttribDivisor(2, 1);

        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, 28, (void*)12); // color
        gl.VertexAttribDivisor(3, 1);

        gl.EnableVertexAttribArray(4);
        gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, 28, (void*)24); // glow
        gl.VertexAttribDivisor(4, 1);

        gl.BindVertexArray(0);

        // Edge buffer — layout per vertex: x, y, r, g, b, a, t, side = 8 floats
        _edgeVao = gl.GenVertexArray();
        gl.BindVertexArray(_edgeVao);

        _edgeVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _edgeVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, 0, null, BufferUsageARB.DynamicDraw);

        _edgeEbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _edgeEbo);
        gl.BufferData(BufferTargetARB.ElementArrayBuffer, 0, null, BufferUsageARB.DynamicDraw);

        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 32, (void*)0); // pos
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 32, (void*)8); // color
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 32, (void*)24); // t
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, 32, (void*)28); // side

        gl.BindVertexArray(0);
    }

    private unsafe void DrawEdgesGl(int w, int h, float time)
    {
        var gl = _gl!;
        var proj = BuildProjection(w, h);

        var vertIdx = 0;
        var indexIdx = 0;

        (double R, double G, double B)[] levelColors =
        [
            (0.35, 0.45, 0.85), 
            (0.15, 0.70, 0.60), 
            (0.95, 0.45, 0.55), 
            (0.60, 0.40, 0.80), 
            (0.90, 0.65, 0.20), 
            (0.40, 0.65, 0.85), 
            (0.55, 0.70, 0.35), 
            (0.85, 0.55, 0.40), 
        ];

        lock (_lock)
        {
            var selectedNode = _foregroundNodes.FirstOrDefault();
            var highlighted = BuildHighlighted(selectedNode, _hoverNode);
            var hasFg = (selectedNode ?? _hoverNode) != null;

            foreach (var (package, deps) in _dependencyMap)
            {
                if (!_positions.TryGetValue(package, out var fromPos)) continue;
                var isNodeHighlighted = highlighted.Contains(package);

                var fromLevel = _levels.GetValueOrDefault(package, 0);
                var fromC = levelColors[Math.Min(fromLevel, levelColors.Length - 1)];

                foreach (var dep in deps)
                {
                    if (!_positions.TryGetValue(dep, out var toPos)) continue;
                    var isEdgeHighlighted = isNodeHighlighted && highlighted.Contains(dep);

                    var toLevel = _levels.GetValueOrDefault(dep, 0);
                    var toC = levelColors[Math.Min(toLevel, levelColors.Length - 1)];

                    var bA = hasFg ? (isEdgeHighlighted ? 1.0f : 0.08f) : 0.25f;

                    var fromScale = _nodeScales.GetValueOrDefault(package, 1f);
                    var toScale = _nodeScales.GetValueOrDefault(dep, 1f);
                    AddCurvedEdge(ref vertIdx, ref indexIdx, fromPos.X, fromPos.Y, toPos.X, toPos.Y, 22f * fromScale, 22f * toScale,
                        (float)fromC.R, (float)fromC.G, (float)fromC.B, bA,
                        (float)toC.R, (float)toC.G, (float)toC.B, bA);
                }
            }
        }

        if (indexIdx == 0) return;

        var shader = _usePerformanceShaders ? _edgeShaderPerf : _edgeShader;
        gl.UseProgram(shader);
        SetUniformMat4(shader, "uProjection", proj);
        if (!_usePerformanceShaders)
            gl.Uniform1(gl.GetUniformLocation(shader, "uTime"), time);

        gl.BindVertexArray(_edgeVao);
        
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _edgeVbo);
        fixed (float* p = _edgeVertexData)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertIdx * 4),
                p, BufferUsageARB.StreamDraw);

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _edgeEbo);
        fixed (uint* p = _edgeIndexData)
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indexIdx * 4),
                p, BufferUsageARB.StreamDraw);

        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One); // Additive-ish for glow
        gl.DrawElements(PrimitiveType.Triangles, (uint)indexIdx, DrawElementsType.UnsignedInt, (void*)0);
        gl.BindVertexArray(0);
    }

    private void AddCurvedEdge(ref int vertIdx, ref int indexIdx, float x1, float y1, float x2, float y2, float r1, float r2,
        float r1C, float g1C, float b1C, float a1C, float r2C, float g2C, float b2C, float a2C)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var distSq = dx * dx + dy * dy;
        if (distSq < 0.001f) return;
        var dist = (float)Math.Sqrt(distSq);

        var nx = dx / dist;
        var ny = dy / dist;

        var sx = x1 + nx * r1;
        var sy = y1 + ny * r1;
        var ex = x2 - nx * r2;
        var ey = y2 - ny * r2;

        var cx = (sx + ex) / 2f + (ey - sy) * 0.15f;
        var cy = (sy + ey) / 2f - (ex - sx) * 0.15f;

        const int segments = 7;
        
        var perpX = -(ey - sy);
        var perpY = ex - sx;
        var pLen = (float)Math.Sqrt(perpX * perpX + perpY * perpY);
        if (pLen > 0.0001f) { perpX /= pLen; perpY /= pLen; }

        const float thickness = 2.0f;
        
        var requiredVerts = vertIdx + (segments + 1) * 2 * 8;
        if (requiredVerts > _edgeVertexData.Length)
            Array.Resize(ref _edgeVertexData, Math.Max(_edgeVertexData.Length * 2, requiredVerts));

        var requiredIndices = indexIdx + segments * 6;
        if (requiredIndices > _edgeIndexData.Length)
            Array.Resize(ref _edgeIndexData, Math.Max(_edgeIndexData.Length * 2, requiredIndices));

        var baseVertIdx = vertIdx / 8;

        for (var i = 0; i <= segments; i++)
        {
            var t = i / (float)segments;
            var omt = 1f - t;
            
            // Quadratic Bezier
            var vx = omt * omt * sx + 2f * omt * t * cx + t * t * ex;
            var vy = omt * omt * sy + 2f * omt * t * cy + t * t * ey;

            var r = r1C * omt + r2C * t;
            var g = g1C * omt + g2C * t;
            var b = b1C * omt + b2C * t;
            var a = a1C * omt + a2C * t;

            // Lower vertex (side -1)
            _edgeVertexData[vertIdx++] = vx - perpX * thickness;
            _edgeVertexData[vertIdx++] = vy - perpY * thickness;
            _edgeVertexData[vertIdx++] = r;
            _edgeVertexData[vertIdx++] = g;
            _edgeVertexData[vertIdx++] = b;
            _edgeVertexData[vertIdx++] = a;
            _edgeVertexData[vertIdx++] = t;
            _edgeVertexData[vertIdx++] = -1f;

            // Upper vertex (side 1)
            _edgeVertexData[vertIdx++] = vx + perpX * thickness;
            _edgeVertexData[vertIdx++] = vy + perpY * thickness;
            _edgeVertexData[vertIdx++] = r;
            _edgeVertexData[vertIdx++] = g;
            _edgeVertexData[vertIdx++] = b;
            _edgeVertexData[vertIdx++] = a;
            _edgeVertexData[vertIdx++] = t;
            _edgeVertexData[vertIdx++] = 1f;

            if (i >= segments) continue;
            var v0 = (uint)(baseVertIdx + i * 2);
            var v1 = v0 + 1;
            var v2 = v0 + 2;
            var v3 = v0 + 3;

            _edgeIndexData[indexIdx++] = v0;
            _edgeIndexData[indexIdx++] = v1;
            _edgeIndexData[indexIdx++] = v2;
            _edgeIndexData[indexIdx++] = v1;
            _edgeIndexData[indexIdx++] = v3;
            _edgeIndexData[indexIdx++] = v2;
        }
    }

    private unsafe void DrawNodesGl(int w, int h)
    {
        var gl = _gl!;
        var proj = BuildProjection(w, h);

        (double R, double G, double B)[] levelColors =
        [
            (0.35, 0.45, 0.85),
            (0.15, 0.70, 0.60),
            (0.95, 0.45, 0.55), 
            (0.60, 0.40, 0.80), 
            (0.90, 0.65, 0.20), 
            (0.40, 0.65, 0.85), 
            (0.55, 0.70, 0.35), 
            (0.85, 0.55, 0.40), 
        ];

        var instances = _nodeInstanceData;
        var instanceIdx = 0;

        lock (_lock)
        {
            var selectedNode = _foregroundNodes.FirstOrDefault();
            var highlighted = BuildHighlighted(selectedNode, _hoverNode);
            var hasFg = (selectedNode ?? _hoverNode) != null;

            if (_positions.Count * 7 > instances.Length)
                Array.Resize(ref _nodeInstanceData, _positions.Count * 7);
            instances = _nodeInstanceData;

            foreach (var (name, pos) in _positions)
            {
                var level = _levels.GetValueOrDefault(name, 0);
                var c = levelColors[Math.Min(level, levelColors.Length - 1)];

                var isDimmed = hasFg && !highlighted.Contains(name);
                var isSelected = name == selectedNode;
                var isHovered = name == _hoverNode;
                var isHighlight = highlighted.Contains(name) && !isSelected && !isHovered;

                float r, g, b;
                if (isDimmed)
                {
                    r = 0.12f;
                    g = 0.12f;
                    b = 0.12f;
                }
                else if (isSelected)
                {
                    r = 1f;
                    g = 1f;
                    b = 0.8f;
                }
                else if (isHovered)
                {
                    r = 0.6f;
                    g = 0.9f;
                    b = 1f;
                }
                else
                {
                    r = (float)c.R;
                    g = (float)c.G;
                    b = (float)c.B;
                }

                var glow = isSelected ? 1f : isHovered ? 0.8f : isHighlight ? 0.6f : isDimmed ? 0f : 0.25f;
                var scale = _nodeScales.GetValueOrDefault(name, 1f);
                var radius = 22f * scale;

                // x, y, radius, r, g, b, glow
                instances[instanceIdx++] = pos.X;
                instances[instanceIdx++] = pos.Y;
                instances[instanceIdx++] = radius;
                instances[instanceIdx++] = r;
                instances[instanceIdx++] = g;
                instances[instanceIdx++] = b;
                instances[instanceIdx++] = glow;
            }
        }

        if (instanceIdx == 0) return;

        var count = (uint)(instanceIdx / 7);

        var shader = _usePerformanceShaders ? _nodeShaderPerf : _nodeShader;
        gl.UseProgram(shader);
        SetUniformMat4(shader, "uProjection", proj);

        gl.BindVertexArray(_nodeVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
        fixed (float* p = instances)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(instanceIdx * 4),
                p, BufferUsageARB.StreamDraw);

        gl.Enable(EnableCap.Blend);

        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, count);


        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, count);

        gl.BindVertexArray(0);
    }

    private System.Numerics.Matrix4x4 BuildProjection(int w, int h)
    {
        var left = (float)(-w / 2.0 / _zoom - _panX / _zoom);
        var right = (float)(w / 2.0 / _zoom - _panX / _zoom);
        var bottom = (float)(h / 2.0 / _zoom - _panY / _zoom);
        var top = (float)(-h / 2.0 / _zoom - _panY / _zoom);

        return System.Numerics.Matrix4x4.CreateOrthographicOffCenter(
            left, right, bottom, top, -1f, 1f);
    }

    private unsafe void SetUniformMat4(uint prog, string name, System.Numerics.Matrix4x4 mat)
    {
        var loc = _gl!.GetUniformLocation(prog, name);
        var m = mat;
        _gl.UniformMatrix4(loc, 1, false, (float*)&m);
    }

    private HashSet<string> BuildHighlighted(string? selectedNode, string? hoverNode)
    {
        HashSet<string> highlighted = [];
        if (selectedNode == null && hoverNode == null) return highlighted;

        if (selectedNode != null)
        {
            highlighted.Add(selectedNode);
            if (_dependencyMap.TryGetValue(selectedNode, out var deps))
                foreach (var d in deps)
                    highlighted.Add(d);
            foreach (var (parent, children) in _dependencyMap)
                if (children.Contains(selectedNode))
                    highlighted.Add(parent);
        }

        if (hoverNode == null) return highlighted;
        {
            highlighted.Add(hoverNode);
            if (_dependencyMap.TryGetValue(hoverNode, out var hDeps))
                foreach (var d in hDeps)
                    highlighted.Add(d);
            foreach (var (parent, children) in _dependencyMap)
                if (children.Contains(hoverNode))
                    highlighted.Add(parent);
        }

        return highlighted;
    }

    private static float GetTargetScale(string name, string? selectedNode, HashSet<string> highlighted)
    {
        if (selectedNode == null) return 1f;
        return highlighted.Contains(name) ? 1f : 0.4f;
    }

    private void AnimateScales()
    {
        lock (_lock)
        {
            var selectedNode = _foregroundNodes.FirstOrDefault();
            var highlighted = BuildHighlighted(selectedNode, _hoverNode);
            const float speed = 0.12f;

            foreach (var name in _positions.Keys)
            {
                var target = GetTargetScale(name, selectedNode ?? _hoverNode, highlighted);
                var current = _nodeScales.GetValueOrDefault(name, 1f);
                _nodeScales[name] = current + (target - current) * speed;
            }
        }
    }

    private bool ScalesAreAnimating()
    {
        lock (_lock)
        {
            var selectedNode = _foregroundNodes.FirstOrDefault();
            var highlighted = BuildHighlighted(selectedNode, _hoverNode);

            return (from name in _positions.Keys
                let current = _nodeScales.GetValueOrDefault(name, 1f)
                let target = GetTargetScale(name, selectedNode ?? _hoverNode, highlighted)
                where Math.Abs(current - target) > 0.005f
                select current).Any();
        }
    }

    public string? GetHoverNode()
    {
        lock (_lock) return _hoverNode;
    }

    public void SetHoverNode(string? packageName)
    {
        lock (_lock)
        {
            if (_lockHover && _hoverNode != null && packageName != _hoverNode) return;
            if (_hoverNode == packageName) return;
            _hoverNode = packageName;
        }

        QueueDraw();
        _labelOverlay?.QueueDraw();
    }

    public void UpdateData(string rootPackage, Dictionary<string, List<string>> dependencyMap)
    {
        lock (_lock)
        {
            _rootPackage = rootPackage;
            _dependencyMap = dependencyMap;
            CalculateInitialLayout();
            RunSimulationStepForceAtlas2(150);
        }

        QueueDraw();
        _labelOverlay?.QueueDraw();
    }

    public void SetTransform(double zoom, double panX, double panY)
    {
        _zoom = zoom;
        _panX = panX;
        _panY = panY;
        QueueDraw();
        _labelOverlay?.QueueDraw();
    }

    public string? GetPackageAt(double x, double y)
    {
        var w = GetAllocatedWidth();
        var h = GetAllocatedHeight();
        var gx = (x - w / 2.0 - _panX) / _zoom;
        var gy = (y - h / 2.0 - _panY) / _zoom;

        const double radius = 22.0;
        lock (_lock)
        {
            foreach (var (name, pos) in _positions)
            {
                var dx = gx - pos.X;
                var dy = gy - pos.Y;
                if (dx * dx + dy * dy <= radius * radius)
                    return name;
            }
        }

        return null;
    }

    public void DrawLabels(Context cr, int w, int h)
    {
        if (_zoom < 0.3) return;

        lock (_lock)
        {
            if (string.IsNullOrEmpty(_rootPackage)) return;

            cr.Translate(w / 2.0 + _panX, h / 2.0 + _panY);
            cr.Scale(_zoom, _zoom);

            var left = (-w / 2.0 - _panX) / _zoom;
            var right = (w / 2.0 - _panX) / _zoom;
            var top = (-h / 2.0 - _panY) / _zoom;
            var bottom = (h / 2.0 - _panY) / _zoom;

            var selectedNode = _foregroundNodes.FirstOrDefault();
            var hasFg = (selectedNode ?? _hoverNode) != null;
            var highlighted = BuildHighlighted(selectedNode, _hoverNode);

            const float radius = 22f;

            foreach (var (name, pos) in _positions)
            {
                if (pos.X < left || pos.X > right || pos.Y < top || pos.Y > bottom) continue;

                var scale = _nodeScales.GetValueOrDefault(name, 1f);
                if (scale < 0.2f) continue;

                var isDimmed = hasFg && !highlighted.Contains(name);
                var isSelected = name == selectedNode;
                var isHovered = name == _hoverNode;

                cr.NewPath();

                if (isSelected)
                    cr.SetSourceRgba(1, 1, 0.4, 1);
                else if (isHovered)
                    cr.SetSourceRgba(0.4, 0.8, 1, 1);
                else if (isDimmed)
                    cr.SetSourceRgba(1, 1, 1, 0.25 * scale);
                else
                    cr.SetSourceRgba(1, 1, 1, 0.85 * scale);

                cr.SetFontSize(10 / _zoom);
                cr.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                cr.TextExtents(name, out var te);
                cr.MoveTo(pos.X - te.Width / 2, pos.Y + radius + te.Height + 5 / _zoom);
                cr.ShowText(name);
            }
        }
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
        _nodeScales.Clear();

        var leafCounts = new Dictionary<string, int>();
        CalculateLeafCounts(_rootPackage, childrenMap, leafCounts);

        const float levelRadius = 500f;
        _positions[_rootPackage] = new Point { X = 0, Y = 0 };
        _velocities[_rootPackage] = new Point { X = 0, Y = 0 };
        _nodeScales[_rootPackage] = 1f;

        PositionNodesRadial(_rootPackage, 0, 2 * (float)Math.PI, childrenMap, leafCounts, levelRadius);
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
        const float angularBuffer = 0.035f;
        var availableAngle = maxAngle - minAngle;
        var totalBuffer = angularBuffer * (children.Count - 1);
        var effectiveBuffer = (totalBuffer > availableAngle * 0.5f)
            ? (availableAngle * 0.5f / Math.Max(1, children.Count - 1))
            : angularBuffer;

        foreach (var child in children)
        {
            var childLeaves = leafCounts[child];
            var angleSpan = (availableAngle - (children.Count - 1) * effectiveBuffer) *
                            (childLeaves / (float)totalLeaves);
            var angle = currentAngle + angleSpan / 2f;

            _positions[child] = new Point
                { X = (float)(Math.Cos(angle) * radius), Y = (float)(Math.Sin(angle) * radius) };
            _velocities[child] = new Point { X = 0, Y = 0 };
            _nodeScales[child] = 1f;

            PositionNodesRadial(child, currentAngle, currentAngle + angleSpan, childrenMap, leafCounts, radius + 500f);
            currentAngle += angleSpan + effectiveBuffer;
        }
    }

    private void RunSimulationStepForceAtlas2(int iterations)
    {
        if (_positions.Count == 0) return;

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

        var hasForeground = _foregroundNodes.Count > 0;

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

                    var dist = MathF.Sqrt(distSq);
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
                    var dist = MathF.Sqrt(distSq);

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
                var dist = MathF.Sqrt(distSq);

                var force = kG * dist * (degrees[i] + 1);
                fx[i] -= (x / dist) * force;
                fy[i] -= (y / dist) * force;
            }

            // Update positions
            float totalVelocity = 0;
            for (var i = 0; i < nodes.Count; i++)
            {
                if (i == rootIndex && !hasForeground) continue;

                vx[i] = (vx[i] + fx[i] * timeStep) * damping;
                vy[i] = (vy[i] + fy[i] * timeStep) * damping;
                var mag2 = vx[i] * vx[i] + vy[i] * vy[i];
               
                totalVelocity +=  MathF.Sqrt(mag2);
                px[i] += vx[i] * timeStep;
                py[i] += vy[i] * timeStep;
            }

            if (totalVelocity < 0.005f * nodes.Count) break;
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            _positions[nodes[i]] = new Point { X = px[i], Y = py[i] };
            _velocities[nodes[i]] = new Point { X = vx[i], Y = vy[i] };
        }
    }

    public override void Dispose()
    {
        MakeCurrent();
        if (_gl != null)
        {
            _gl.DeleteVertexArray(_nodeVao);
            _gl.DeleteVertexArray(_edgeVao);
            _gl.DeleteBuffer(_nodeVbo);
            _gl.DeleteBuffer(_instanceVbo);
            _gl.DeleteBuffer(_edgeVbo);
            _gl.DeleteProgram(_nodeShader);
            _gl.DeleteProgram(_edgeShader);
        }

        base.Dispose();
    }
}