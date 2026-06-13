namespace Starfish.Services;

public class QuadTree(float x0, float y0, float x1, float y1)
{
    private float _cx, _cy, _mass;
    private float _lx, _ly, _lm;
    private int _singleIndex = -1;
    
    private readonly float _x0 = x0, _x1 = x1;

    private QuadTree? _nw, _ne, _sw, _se;

    private QuadTree GetOrCreateChild(float x, float y)
    {
        var mx = (_x0 + _x1) * 0.5f;
        var my = (y0 + y1) * 0.5f;
        if (x < mx)
            return y < my
                ? _sw ??= new QuadTree(_x0, y0, mx, my)
                : _nw ??= new QuadTree(_x0, my, mx, y1);
        return y < my
            ? _se ??= new QuadTree(mx, y0, _x1, my)
            : _ne ??= new QuadTree(mx, my, _x1, y1);
    }
    
    public void Insert(float x, float y, float mass, int index,
        Stack<(float x, float y, float mass, int index, QuadTree node)> work)
    {
        work.Clear();
        work.Push((x, y, mass, index, this));

        while (work.Count > 0)
        {
            var (wx, wy, wm, wi, node) = work.Pop();

            if (node._mass == 0f)
            {
                node._cx = wx;
                node._cy = wy;
                node._mass = wm;
                node._lx = wx;
                node._ly = wy;
                node._lm = wm;
                node._singleIndex = wi;
                continue;
            }

            var total = node._mass + wm;
            node._cx = (node._cx * node._mass + wx * wm) / total;
            node._cy = (node._cy * node._mass + wy * wm) / total;
            node._mass = total;

            if (node._singleIndex >= 0)
            {
                work.Push((node._lx, node._ly, node._lm, node._singleIndex,
                    node.GetOrCreateChild(node._lx, node._ly)));
                node._singleIndex = -1;
            }

            work.Push((wx, wy, wm, wi, node.GetOrCreateChild(wx, wy)));
        }
    }

    public void AccumulateRepulsion(
        int queryIndex, float x, float y, float mass,
        float kR, float thetaSq,
        ref float fx, ref float fy,
        Random rng,
        Stack<QuadTree> stack)
    {
        stack.Clear();
        stack.Push(this);

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            if (node._mass == 0f) continue;
            if (node._singleIndex == queryIndex) continue;

            var dx = x - node._cx;
            var dy = y - node._cy;
            var distSq = dx * dx + dy * dy;
            var size = node._x1 - node._x0;

            var applyNow = node._singleIndex >= 0
                           || (distSq > 0f && size * size / distSq < thetaSq);

            if (applyNow)
            {
                if (distSq < 1.0f)
                {
                    dx = (float)(rng.NextDouble() - 0.5);
                    dy = (float)(rng.NextDouble() - 0.5);
                    distSq = dx * dx + dy * dy + 0.1f;
                }

                var invDist = 1f / MathF.Sqrt(distSq);
                var force = kR * mass * node._mass * invDist;
                fx += dx * invDist * force;
                fy += dy * invDist * force;
            }
            else
            {
                if (node._nw != null) stack.Push(node._nw);
                if (node._ne != null) stack.Push(node._ne);
                if (node._sw != null) stack.Push(node._sw);
                if (node._se != null) stack.Push(node._se);
            }
        }
    }
}