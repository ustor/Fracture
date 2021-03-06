﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Evil;
using Squared.Util;

namespace Squared.Render {
    public interface IUniformBinding : IDisposable {
        Type   Type    { get; }
        string Name    { get; }
        Effect Effect  { get; }
        bool   IsDirty { get; }

        void Flush ();
    }

    public static class UniformBindingExtensions {
        public static UniformBinding<T> Cast<T> (this IUniformBinding iub)
            where T : struct
        {
            return (UniformBinding<T>)iub;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    unsafe delegate int SetRawDelegate (void* _this, void* a, void* b, uint c, uint d);

    public unsafe partial class UniformBinding<T> : IUniformBinding 
        where T : struct
    {
        public class ValueContainer {
            public T Current;
        }

        public class Storage : SafeBuffer {
            public Storage () 
                : base(true) 
            {
                // HACK: If this isn't big enough, you screwed up
                const int size = 1024 * 4;
                Initialize(size);
                SetHandle(Marshal.AllocHGlobal(size));
            }

            protected override bool ReleaseHandle () {
                Marshal.FreeHGlobal(DangerousGetHandle());
                return true;
            }
        }

        private readonly Fixup[]     Fixups;
        private readonly uint        UploadSize;

        private readonly ValueContainer _ValueContainer = new ValueContainer();
        // The latest value is written into this buffer
        private readonly SafeBuffer  ScratchBuffer;
        // And then transferred and mutated in this buffer before being sent to D3D
        private readonly SafeBuffer  UploadBuffer;

        private readonly SetRawDelegate pSetRawValue;

        public  bool   IsDirty    { get; private set; }
        public  bool   IsDisposed { get; private set; }
        public  Effect Effect     { get; private set; }
        public  string Name       { get; private set; }
        public  Type   Type       { get; private set; }

        #region Direct3D
#if !SDL2

        private readonly ID3DXEffect pEffect;
        private readonly void*       pUnboxedEffect;
        private readonly void*       hParameter;

        /// <summary>
        /// Bind a single named uniform of the effect as type T.
        /// </summary>
        private UniformBinding (Effect effect, ID3DXEffect pEffect, void* hParameter) {
            Type = typeof(T);

            Effect = effect;
            this.pEffect = pEffect;
            this.pUnboxedEffect = effect.GetUnboxedID3DXEffect();
            this.hParameter = hParameter;

            var layout = new Layout(Type, pEffect, hParameter);

            Fixups = layout.Fixups;
            UploadSize = layout.UploadSize;

            ScratchBuffer = new Storage();
            UploadBuffer = new Storage();
            IsDirty = false;

            var iface = typeof(ID3DXEffect);
            var firstSlot = Marshal.GetStartComSlot(iface);

            // HACK: Bypass totally broken remoting wrappers by directly pulling the method out of the vtable
            const int SetRawValueSlot = 75;
            var pComFunction = Evil.COMUtils.AccessVTable(
                pUnboxedEffect, 
                (uint)((SetRawValueSlot + firstSlot) * sizeof(void*))
            );
            pSetRawValue = Marshal.GetDelegateForFunctionPointer<SetRawDelegate>(new IntPtr(pComFunction));

            UniformBinding.Register(effect, this);
        }

        public static UniformBinding<T> TryCreate (Effect effect, ID3DXEffect pEffect, string uniformName) {
            if (effect == null)
                return null;
            if (pEffect == null)
                return null;

            var hParameter = pEffect.GetParameterByName(null, uniformName);
            if (hParameter == null)
                return null;

            return new UniformBinding<T>(effect, pEffect, hParameter);
        }

#endif
        #endregion

        #region SDL2
        #if SDL2
        #endif
        #endregion

        /// <summary>
        /// If you retain this you are a bad person and I'm ashamed of you! Don't do that!!!
        /// </summary>
        public ValueContainer Value {
            get {
                IsDirty = true;
                return _ValueContainer;
            }
        }

        private void InPlaceTranspose (float* pMatrix) {
            float temp = pMatrix[4];
            pMatrix[4] = pMatrix[1];
            pMatrix[1] = temp;

            temp = pMatrix[8];
            pMatrix[8] = pMatrix[2];
            pMatrix[2] = temp;

            temp = pMatrix[12];
            pMatrix[12] = pMatrix[3];
            pMatrix[3] = temp;

            temp = pMatrix[9];
            pMatrix[9] = pMatrix[6];
            pMatrix[6] = temp;

            temp = pMatrix[13];
            pMatrix[13] = pMatrix[7];
            pMatrix[7] = temp;

            temp = pMatrix[14];
            pMatrix[14] = pMatrix[11];
            pMatrix[11] = temp;
        }

        public void Flush () {
            if (!IsDirty)
                return;

            ScratchBuffer.Write<T>(0, _ValueContainer.Current);

            var pScratch = ScratchBuffer.DangerousGetHandle();
            var pUpload  = UploadBuffer.DangerousGetHandle();

            // Fix-up matrices because the in-memory order is transposed :|
            foreach (var fixup in Fixups) {
                var pSource = (pScratch + fixup.FromOffset);
                var pDest = (pUpload + fixup.ToOffset);

                if (fixup.TransposeMatrix)
                    InPlaceTranspose((float*)pSource);

                Buffer.MemoryCopy(
                    pSource.ToPointer(),
                    pDest.ToPointer(),
                    fixup.DataSize, fixup.DataSize
                );
            }

#if SDL2
            throw new NotImplementedException("Write parameters buffer to GL uniform buffer");
#else
            // HACK: Bypass the COM wrapper and invoke directly from the vtable.
            var hr = pSetRawValue(pUnboxedEffect, hParameter, pUpload.ToPointer(), 0, UploadSize);
            Marshal.ThrowExceptionForHR(hr);
            // pEffect.SetRawValue(hParameter, pUpload.ToPointer(), 0, UploadSize);
#endif

            IsDirty = false;
        }

        private static bool IsStructure (Type type) {
            return type.IsValueType && !type.IsPrimitive;
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            ScratchBuffer.Dispose();
            UploadBuffer.Dispose();

#if SDL2
#else
            Marshal.ReleaseComObject(pEffect);
#endif
        }
    }

    public static class UniformBinding {
        // Making a dictionary larger increases performance
        private const int BindingDictionaryCapacity = 4096;

        private static readonly Dictionary<Effect, List<IUniformBinding>> Bindings =
            new Dictionary<Effect, List<IUniformBinding>>(new ReferenceComparer<Effect>());

        public static void FlushEffect (Effect effect) {
            lock (Bindings) {
                List<IUniformBinding> bindings;
                if (!Bindings.TryGetValue(effect, out bindings))
                    return;

                foreach (var binding in bindings)
                    binding.Flush();
            }
        }

        internal static void Register (Effect effect, IUniformBinding binding) {
            List<IUniformBinding> bindings;
            lock (Bindings) {
                if (!Bindings.TryGetValue(effect, out bindings))
                    Bindings[effect] = bindings = new List<IUniformBinding>();

                bindings.Add(binding);
            }
        }
    }
}
