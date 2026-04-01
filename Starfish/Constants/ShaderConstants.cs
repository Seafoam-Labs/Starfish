namespace Starfish.Constants;

public static class ShaderConstants
{
    public const string NodeVert = """
                                   #version 330 core
                                   layout(location = 0) in vec2 aQuadPos;    // unit quad vertex (-1..1)
                                   layout(location = 1) in vec2 iPos;         // instance: world position
                                   layout(location = 2) in float iRadius;     // instance: radius
                                   layout(location = 3) in vec3 iColor;       // instance: color
                                   layout(location = 4) in float iGlow;       // instance: 0=normal 1=selected

                                   uniform mat4 uProjection;

                                   out vec2 vUV;        // -1..1 within quad
                                   out vec3 vColor;
                                   out float vGlow;

                                   void main()
                                   {
                                       vUV    = aQuadPos;
                                       vColor = iColor;
                                       vGlow  = iGlow;

                                       // scale unit quad to node size and move to world position
                                       vec2 worldPos = iPos + aQuadPos * iRadius * 2.2; // 2.2 gives glow room
                                       gl_Position = uProjection * vec4(worldPos, 0.0, 1.0);
                                   }
                                   """;

    public const string NodeFrag = """
                                   #version 330 core
                                   in vec2 vUV;
                                   in vec3 vColor;
                                   in float vGlow; // can be removed if unused
                                   
                                   out vec4 FragColor;
                                   
                                   void main()
                                   {
                                       float dist = length(vUV); // 0=center, 1=quad edge
                                   
                                       // hard circle edge at ~0.45 of the 2.2x quad
                                       float nodeEdge = 0.45;
                                       float circle   = 1.0 - smoothstep(nodeEdge - 0.02, nodeEdge + 0.02, dist);
                                   
                                       vec3 color = vColor;
                                   
                                       // subtle inner highlight top-left
                                       float highlight = (1.0 - smoothstep(0.0, 0.35, length(vUV - vec2(-0.2, -0.2)))) * 0.3;
                                       color += highlight;
                                   
                                       float alpha = circle;
                                       if (alpha < 0.01) discard;
                                   
                                       FragColor = vec4(color, alpha);
                                   }
                                   """;

    public const string EdgeVert = """
                                   #version 330 core
                                   layout(location = 0) in vec2 aPos;
                                   layout(location = 1) in vec4 aColor;

                                   uniform mat4 uProjection;

                                   out vec4 vColor;

                                   void main()
                                   {
                                       vColor      = aColor;
                                       gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
                                   }
                                   """;

   public const string EdgeFrag = """
                                    #version 330 core
                                    in vec4 vColor;
                                    out vec4 FragColor;
                                    void main() { FragColor = vColor; }
                                    """;
}