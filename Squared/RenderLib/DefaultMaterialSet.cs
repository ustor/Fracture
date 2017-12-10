﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System.Threading;
using Microsoft.Xna.Framework.Content;
using System.Reflection;
using Squared.Render.Convenience;
using Squared.Util;
using System.Runtime.InteropServices;

namespace Squared.Render {
    [StructLayout(LayoutKind.Sequential)]
    public struct ViewTransform {
        public Vector2 Scale;
        public Vector2 Position;
        public Matrix Projection;
        public Matrix ModelView;

        public static readonly ViewTransform Default = new ViewTransform {
            Scale = Vector2.One,
            Position = Vector2.Zero,
            Projection = Matrix.Identity,
            ModelView = Matrix.Identity
        };

        public static ViewTransform CreateOrthographic (Viewport viewport) {
            return CreateOrthographic(viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
        }

        public static ViewTransform CreateOrthographic (int screenWidth, int screenHeight, float zNearPlane = 0, float zFarPlane = 1) {
            return CreateOrthographic(0, 0, screenWidth, screenHeight, zNearPlane, zFarPlane);
        }

        public static ViewTransform CreateOrthographic (int x, int y, int width, int height, float zNearPlane = 0, float zFarPlane = 1) {
            float offsetX = -0.0f;
            float offsetY = -0.0f;
            float offsetX2 = offsetX;
            float offsetY2 = offsetY;
            return new ViewTransform {
                Scale = Vector2.One,
                Position = Vector2.Zero,
                Projection = Matrix.CreateOrthographicOffCenter(offsetX, width + offsetX2, height + offsetY2, offsetY, zNearPlane, zFarPlane),
                ModelView = Matrix.Identity
            };
        }

        public bool Equals (ref ViewTransform rhs) {
            return (Scale == rhs.Scale) &&
                (Position == rhs.Position) &&
                (Projection == rhs.Projection) &&
                (ModelView == rhs.ModelView);
        }
    }

    public class DefaultMaterialSet : MaterialSetBase {
        protected struct MaterialCacheKey {
            public readonly Material Material;
            public readonly RasterizerState RasterizerState;
            public readonly DepthStencilState DepthStencilState;
            public readonly BlendState BlendState;

            public MaterialCacheKey (Material material, RasterizerState rasterizerState, DepthStencilState depthStencilState, BlendState blendState) {
                Material = material;
                RasterizerState = rasterizerState;
                DepthStencilState = depthStencilState;
                BlendState = blendState;
            }

            private static int HashNullable<T> (T o) where T : class {
                if (o == null)
                    return 0;
                else
                    return o.GetHashCode();
            }

            public bool Equals (ref MaterialCacheKey rhs) {
                return (Material == rhs.Material) &&
                    (RasterizerState == rhs.RasterizerState) &&
                    (DepthStencilState == rhs.DepthStencilState) &&
                    (BlendState == rhs.BlendState);
            }

            public override bool Equals (object obj) {
                if (obj is MaterialCacheKey) {
                    var mck = (MaterialCacheKey)obj;
                    return Equals(ref mck);
                } else
                    return base.Equals(obj);
            }

            public override int GetHashCode () {
                return Material.GetHashCode() ^
                    HashNullable(RasterizerState) ^
                    HashNullable(DepthStencilState) ^
                    HashNullable(BlendState);
            }
        }

        protected class MaterialCacheKeyComparer : IEqualityComparer<MaterialCacheKey> {
            public bool Equals (MaterialCacheKey x, MaterialCacheKey y) {
                return x.Equals(ref y);
            }

            public int GetHashCode (MaterialCacheKey obj) {
                return obj.GetHashCode();
            }
        }
        
        public readonly ContentManager BuiltInShaders;
        public readonly ITimeProvider  TimeProvider;

        protected readonly MaterialDictionary<MaterialCacheKey> MaterialCache = new MaterialDictionary<MaterialCacheKey>(
            new MaterialCacheKeyComparer()
        );

        public Material ScreenSpaceBitmap, WorldSpaceBitmap;
        public Material ScreenSpaceShadowedBitmap, WorldSpaceShadowedBitmap;
        public Material ScreenSpaceBitmapWithDiscard, WorldSpaceBitmapWithDiscard;
        public Material ScreenSpaceGeometry, WorldSpaceGeometry;
        public Material ScreenSpaceLightmappedBitmap, WorldSpaceLightmappedBitmap;
        public Material ScreenSpaceHorizontalGaussianBlur5Tap, ScreenSpaceVerticalGaussianBlur5Tap;
        public Material WorldSpaceHorizontalGaussianBlur5Tap, WorldSpaceVerticalGaussianBlur5Tap;
        public Material Clear;

        protected readonly Action<Material, float> _ApplyTimeDelegate;
        protected readonly RefMaterialAction<ViewTransform> _ApplyViewTransformDelegate; 
        protected readonly Stack<ViewTransform> ViewTransformStack = new Stack<ViewTransform>();

        protected ViewTransform? CurrentlyAppliedViewTransform = null;

        public DefaultMaterialSet (IServiceProvider serviceProvider) {
            _ApplyViewTransformDelegate = ApplyViewTransformToMaterial;
            _ApplyTimeDelegate          = ApplyTimeToMaterial;

            TimeProvider = (ITimeProvider)serviceProvider.GetService(typeof(ITimeProvider))
                ?? new DotNetTimeProvider();

#if SDL2 // `Content/SquaredRender/` folder -flibit
            BuiltInShaders = new ContentManager(serviceProvider, "Content/SquaredRender");
#elif MG
            BuiltInShaders = new ContentManager( serviceProvider, "Content" ); // This needs to use ContentManager (ResourceContentManager is for .resx file)
#else
            BuiltInShaders = new ResourceContentManager(serviceProvider, Shaders.ResourceManager);
#endif

            Clear = new Material(
                null, null, 
                new Action<DeviceManager>[] { (dm) => ApplyShaderVariables() }
            );

   
            var bitmapShader = LoadBuiltInShader("SquaredBitmapShader");
            var geometryShader = LoadBuiltInShader("SquaredGeometryShader");
            
            ScreenSpaceBitmap = new Material(
                bitmapShader,
                "ScreenSpaceBitmapTechnique"
            );

            WorldSpaceBitmap = new Material(
                bitmapShader,
                "WorldSpaceBitmapTechnique"
            );
            
            ScreenSpaceShadowedBitmap = new Material(
                bitmapShader,
                "ScreenSpaceShadowedBitmapTechnique"
            );
            ScreenSpaceShadowedBitmap.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 1));
            ScreenSpaceShadowedBitmap.Parameters.ShadowOffset.SetValue(new Vector2(2, 2));

            WorldSpaceShadowedBitmap = new Material(
                bitmapShader,
                "WorldSpaceShadowedBitmapTechnique"
            );
            WorldSpaceShadowedBitmap.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 1));
            WorldSpaceShadowedBitmap.Parameters.ShadowOffset.SetValue(new Vector2(2, 2));

            ScreenSpaceBitmapWithDiscard = new Material(
                bitmapShader,
                "ScreenSpaceBitmapWithDiscardTechnique"
            );

            WorldSpaceBitmapWithDiscard = new Material(
                bitmapShader,
                "WorldSpaceBitmapWithDiscardTechnique"
            );

            ScreenSpaceGeometry = new Material(
                geometryShader,
                "ScreenSpaceUntextured"
            );

            WorldSpaceGeometry = new Material(
                geometryShader,
                "WorldSpaceUntextured"
            );
            
            var lightmapShader = LoadBuiltInShader("Lightmap");

            ScreenSpaceLightmappedBitmap = new Material(
                lightmapShader,
                "ScreenSpaceLightmappedBitmap"
            );

            WorldSpaceLightmappedBitmap = new Material(
                lightmapShader,
                "WorldSpaceLightmappedBitmap"
            );

            var blurShader = LoadBuiltInShader("GaussianBlur");

            ScreenSpaceHorizontalGaussianBlur5Tap = new Material(
                blurShader,
                "ScreenSpaceHorizontalGaussianBlur5Tap"
            );

            ScreenSpaceVerticalGaussianBlur5Tap = new Material(
                blurShader,
                "ScreenSpaceVerticalGaussianBlur5Tap"
            );

            WorldSpaceHorizontalGaussianBlur5Tap = new Material(
                blurShader,
                "WorldSpaceHorizontalGaussianBlur5Tap"
            );

            WorldSpaceVerticalGaussianBlur5Tap = new Material(
                blurShader,
                "WorldSpaceVerticalGaussianBlur5Tap"
            );

            var gds = serviceProvider.GetService(typeof(IGraphicsDeviceService)) as IGraphicsDeviceService;
            if (gds != null)
                ViewTransformStack.Push(ViewTransform.CreateOrthographic(
                    gds.GraphicsDevice.PresentationParameters.BackBufferWidth,
                    gds.GraphicsDevice.PresentationParameters.BackBufferHeight
                ));
            else
                ViewTransformStack.Push(ViewTransform.Default);

            PreallocateBindings();
        }

        private Effect LoadBuiltInShader( string name ) {
#if MG
            return BuiltInShaders.Load<Effect>( name + ".mg" );
#else
            return BuiltInShaders.Load<Effect>( name );
#endif
        }

        public void PreallocateBindings () {
            // Pre-allocate the uniform bindings for our materials on the main thread.
            ForEachMaterial<object>((material, _) => {
                GetUniformBinding<ViewTransform>(material, "Viewport");
            }, null);
        }

        public ViewTransform ViewTransform {
            get {
                return ViewTransformStack.Peek();
            }
            set {
                ViewTransformStack.Pop();
                ViewTransformStack.Push(value);
            }
        }

        public Vector2 ViewportScale {
            get {
                return ViewTransform.Scale;
            }
            set {
                var vt = ViewTransformStack.Peek();
                vt.Scale = value;
                ViewTransform = vt;
            }
        }

        public Vector2 ViewportPosition {
            get {
                return ViewTransform.Position;
            }
            set {
                var vt = ViewTransformStack.Peek();
                vt.Position = value;
                ViewTransform = vt;
            }
        }

        public Matrix ProjectionMatrix {
            get {
                return ViewTransform.Projection;
            }
            set {
                var vt = ViewTransformStack.Peek();
                vt.Projection = value;
                ViewTransform = vt;
            }
        }

        public Matrix ModelViewMatrix {
            get {
                return ViewTransform.ModelView;
            }
            set {
                var vt = ViewTransformStack.Peek();
                vt.ModelView = value;
                ViewTransform = vt;
            }
        }

        /// <summary>
        /// Immediately changes the view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PushViewTransform (ViewTransform viewTransform) {
            ViewTransformStack.Push(viewTransform);
            ApplyViewTransform(ref viewTransform);
        }

        /// <summary>
        /// Immediately changes the view transform of the material set, without waiting for a clear.
        /// </summary>
        public void PushViewTransform (ref ViewTransform viewTransform) {
            ViewTransformStack.Push(viewTransform);
            ApplyViewTransform(ref viewTransform);
        }

        /// <summary>
        /// Immediately restores the previous view transform of the material set, without waiting for a clear.
        /// </summary>
        public ViewTransform PopViewTransform () {
            var result = ViewTransformStack.Pop();
            ApplyShaderVariables();
            return result;
        }

        /// <summary>
        /// Sets the view transform of all material(s) owned by this material set to the ViewTransform field's current value.
        /// Also sets other parameters like Time.
        /// Clear batches automatically call this function for you.
        /// </summary>
        public void ApplyShaderVariables () {
            var vt = ViewTransformStack.Peek();
            ApplyViewTransform(ref vt);

            float timeSeconds = (float)TimeProvider.Seconds;
            ForEachMaterial(_ApplyTimeDelegate, timeSeconds);
        }

        private static void ApplyTimeToMaterial (Material m, float time) {
            if (m.Effect == null)
                return;

            var p = m.Parameters.Time;
            if (p != null)
                p.SetValue(time);
        }

        public void ApplyViewTransformToMaterial (Material m, ref ViewTransform viewTransform) {
            if (m.Effect == null)
                return;

            var ub = GetUniformBinding<ViewTransform>(m, "Viewport");
            if (ub == null)
                return;

            ub.Value.Current = viewTransform;

            // FIXME: WHY IS THIS NECESSARY?
            // m.Flush();
        }

        /// <summary>
        /// Manually sets the view transform of all material(s) owned by this material set without changing the ViewTransform field.
        /// </summary>
        /// <param name="viewTransform">The view transform to apply.</param>
        public void ApplyViewTransform (ref ViewTransform viewTransform) {
            if (
                CurrentlyAppliedViewTransform.HasValue &&
                CurrentlyAppliedViewTransform.Value.Equals(ref viewTransform)
            ) {
                // Already applied. Don't apply again.
                return;
            } else {
                CurrentlyAppliedViewTransform = viewTransform;
            }

            ForEachMaterial(_ApplyViewTransformDelegate, ref viewTransform);
        }

        /// <summary>
        /// Returns a new version of a given material with rasterizer, depth/stencil, and blend state(s) optionally applied to it. This new version is cached.
        /// If no states are provided, the base material is returned.
        /// </summary>
        /// <param name="baseMaterial">The base material.</param>
        /// <param name="rasterizerState">The new rasterizer state, or null.</param>
        /// <param name="depthStencilState">The new depth/stencil state, or null.</param>
        /// <param name="blendState">The new blend state, or null.</param>
        /// <returns>The material with state(s) applied.</returns>
        public Material Get (Material baseMaterial, RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null) {
            if (
                (rasterizerState == null) &&
                (depthStencilState == null) &&
                (blendState == null)
            )
                return baseMaterial;

            var key = new MaterialCacheKey(baseMaterial, rasterizerState, depthStencilState, blendState);
            Material result;
            if (!MaterialCache.TryGetValue(key, out result)) {
                result = baseMaterial.SetStates(rasterizerState, depthStencilState, blendState);
                MaterialCache.Add(key, result);
            }
            return result;
        }

        public Material GetBitmapMaterial (bool worldSpace, RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null) {
            return Get(
                worldSpace ? WorldSpaceBitmap : ScreenSpaceBitmap,
                rasterizerState: rasterizerState,
                depthStencilState: depthStencilState,
                blendState: blendState
            );
        }

        public Material GetGeometryMaterial (bool worldSpace, RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null) {
            return Get(
                worldSpace ? WorldSpaceGeometry : ScreenSpaceGeometry,
                rasterizerState: rasterizerState,
                depthStencilState: depthStencilState,
                blendState: blendState
            );
        }

        public override void Dispose () {
            base.Dispose();

            BuiltInShaders.Dispose();
            MaterialCache.Clear();
        }
    }

    public class DefaultMaterialSetEffectParameters {
        public readonly EffectParameter ViewportPosition, ViewportScale;
        public readonly EffectParameter ProjectionMatrix, ModelViewMatrix;
        public readonly EffectParameter BitmapTextureSize, HalfTexel;
        public readonly EffectParameter Time, ShadowColor, ShadowOffset, LightmapUVOffset;

        public DefaultMaterialSetEffectParameters (Effect effect) {
#if MG
            ViewportPosition = effect.Parameters["Viewport_Position"];
            ViewportScale = effect.Parameters["Viewport_Scale"];
            ProjectionMatrix = effect.Parameters["Viewport_Projection"];
            ModelViewMatrix = effect.Parameters["Viewport_ModelView"];
#else
            var viewport = effect.Parameters["Viewport"];

            if (viewport != null) {
                ViewportPosition = viewport.StructureMembers["Position"];
                ViewportScale = viewport.StructureMembers["Scale"];
                ProjectionMatrix = viewport.StructureMembers["Projection"];
                ModelViewMatrix = viewport.StructureMembers["ModelView"];
            }
#endif

            BitmapTextureSize = effect.Parameters["BitmapTextureSize"];
            HalfTexel = effect.Parameters["HalfTexel"];
            Time = effect.Parameters["Time"];
            ShadowColor = effect.Parameters["ShadowColor"];
            ShadowOffset = effect.Parameters["ShadowOffset"];
            LightmapUVOffset = effect.Parameters["LightmapUVOffset"];
        }
    }

    public struct SquaredGeometryParameters {
        public Vector2   ViewportScale;
        public Vector2   ViewportPosition;
        public Matrix    ProjectionMatrix;
        public Matrix    ModelViewMatrix;
        public Texture2D BasicTexture;
    }

    public struct SquaredBitmapParameters {
        public Vector2   ViewportScale;
        public Vector2   ViewportPosition;
        public Matrix    ProjectionMatrix;
        public Matrix    ModelViewMatrix;
        public Vector2   BitmapTextureSize;
        public Vector2   HalfTexel;
        public Texture2D BitmapTexture;
        public Texture2D SecondTexture;
        public Vector4   ShadowColor;
        public Vector2   ShadowOffset;
    }
}