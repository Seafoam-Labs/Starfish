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

    public const string NodeFragPerformance = """
                                             #version 330 core
                                             in vec2 vUV;
                                             in vec3 vColor;
                                             in float vGlow;

                                             out vec4 FragColor;

                                             void main()
                                             {
                                                 float distSq = dot(vUV, vUV);
                                                 if (distSq > 0.1936) discard; // 0.44 * 0.44
                                                 
                                                 float circle = 1.0 - smoothstep(0.40, 0.44, sqrt(distSq));
                                                 float stroke = (1.0 - smoothstep(0.36, 0.38, sqrt(distSq))) * (smoothstep(0.42, 0.44, sqrt(distSq))) * vGlow;
                                                 
                                                 vec3 finalColor = mix(vColor, vec3(1.0), stroke);
                                                 FragColor = vec4(finalColor, circle);
                                             }
                                             """;

    public const string NodeFrag = """
                                   #version 330 core
                                   in vec2 vUV;
                                   in vec3 vColor;
                                   in float vGlow;

                                   out vec4 FragColor;

                                   void main()
                                   {
                                       float distSq = dot(vUV, vUV);
                                       if (distSq > 1.0) discard;
                                       float dist = sqrt(distSq);
                                       
                                       float circle = 1.0 - smoothstep(0.40, 0.44, dist);
                                       
                                       float innerGradient = mix(0.8, 1.0, 1.0 - smoothstep(0.0, 0.45, dist));
                                       vec3 finalColor = vColor * (innerGradient + 0.15);

                                       float rim = 1.0 - smoothstep(0.38, 0.42, dist);
                                       finalColor += rim * (1.0 - rim) * 0.6; // Simplified rim

                                       vec2 shadowUV = vUV - vec2(0.02, 0.02);
                                       float shadowAlpha = (1.0 - smoothstep(0.40, 0.7, length(shadowUV))) * 0.4; 
                                       
                                       float glowFalloff = 1.0 - smoothstep(0.40, 0.95, dist);
                                       float glow = glowFalloff * (vGlow > 0.5 ? (vGlow * vGlow * 0.7) : 0.0);
                                       
                                       float stroke = (1.0 - smoothstep(0.36, 0.38, dist)) * (smoothstep(0.42, 0.44, dist)) * vGlow;
                                      
                                       float alpha = max(circle, max(shadowAlpha, glow));
                                       
                                       vec3 bgEffectColor = mix(vec3(0.05), vColor * 1.5, step(0.001, glow));
                                       finalColor = mix(bgEffectColor, finalColor, circle);
                                       finalColor = mix(finalColor, vec3(1.0), stroke * 0.8);
                                       
                                       FragColor = vec4(finalColor, alpha);
                                   }
                                   """;

    public const string EdgeVert = """
                                   #version 330 core
                                   layout(location = 0) in vec2 aPos;
                                   layout(location = 1) in vec4 aColor;
                                   layout(location = 2) in float aT;
                                   layout(location = 3) in float aSide;

                                   uniform mat4 uProjection;

                                   out vec4 vColor;
                                   out float vT;
                                   out float vSide;

                                   void main()
                                   {
                                       vColor      = aColor;
                                       vT          = aT;
                                       vSide       = aSide;
                                       gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
                                   }
                                   """;

    public const string EdgeFragPerformance = """
                                             #version 330 core
                                             in vec4 vColor;
                                             in float vT;
                                             in float vSide;
                                             
                                             out vec4 FragColor;
                                             
                                             void main() 
                                             {
                                                 float alpha = vColor.a * (1.0 - abs(vSide));
                                                 if (alpha < 0.05) discard;
                                                 FragColor = vec4(vColor.rgb, alpha);
                                             }
                                             """;

    public const string EdgeFrag = """
                                   #version 330 core
                                   in vec4 vColor;
                                   in float vT;
                                   in float vSide;
                                   
                                   uniform float uTime;
                                   
                                   out vec4 FragColor;
                                   
                                   void main() 
                                   {
                                       float sideFade = 1.0 - abs(vSide);
                                       float taper = smoothstep(0.0, 0.03, vT) * (1.0 - smoothstep(0.97, 1.0, vT));
                                       
                                       // Combined effect calculation
                                       float pulse = sin(vT * 8.0 - uTime * 3.0) * 0.5 + 0.5;
                                       pulse *= pulse; 
                                       
                                       vec4 color = vColor;
                                       color.rgb *= (0.7 + pulse * 0.5);
                                       color.a *= taper * sqrt(sideFade);
                                       
                                       FragColor = color;
                                   }
                                   """;
}