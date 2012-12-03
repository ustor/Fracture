﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Render;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Squared.Game;
using System.Reflection;
using Squared.Util;
using System.Diagnostics;

namespace Squared.Render.Internal {
    public struct VertexBuffer<T> : IDisposable
        where T : struct {

        public readonly ArrayPoolAllocator<T>.Allocation Allocation;
        public int Count;

        public VertexBuffer(ArrayPoolAllocator<T> allocator, int capacity) {
            Allocation = allocator.Allocate(capacity);
            Count = 0;
        }

        public T[] Buffer {
            get {
                return Allocation.Buffer;
            }
        }

        public VertexWriter<T> GetWriter(int capacity) {
            var offset = Count;
            var newCount = Count + capacity;

            if (newCount >= Allocation.Buffer.Length)
                throw new InvalidOperationException();

            // FIXME: This shouldn't be needed!
            Array.Clear(this.Allocation.Buffer, offset, capacity);

            Count = newCount;
            return new VertexWriter<T>(this.Allocation.Buffer, offset, capacity);
        }

        public void Dispose() {
            Count = 0;
        }
    }

    public struct VertexWriter<T>
        where T : struct {

        public readonly T[] Buffer;
        public readonly int Offset;
        public readonly int Size;
        public int Count;

        public VertexWriter(T[] buffer, int offset, int size) {
            Buffer = buffer;
            Offset = offset;
            Size = size;
            Count = 0;
        }

        public void Write (T newVertex) {
            if (Count >= Size)
                throw new InvalidOperationException();

            Buffer[Offset + Count] = newVertex;
            Count += 1;
        }

        public void Write (ref T newVertex) {
            if (Count >= Size)
                throw new InvalidOperationException();

            Buffer[Offset + Count] = newVertex;
            Count += 1;
        }

        public void Write (int index, T newVertex) {
            if (index >= Size)
                throw new InvalidOperationException();

            Count = Math.Max(Count, index + 1);
            index += Offset;

            Buffer[index] = newVertex;
        }

        public void Write (int index, ref T newVertex) {
            if (index >= Size)
                throw new InvalidOperationException();

            Count = Math.Max(Count, index + 1);
            index += Offset;

            Buffer[index] = newVertex;
        }

        public PrimitiveDrawCall<T> GetDrawCall(PrimitiveType type) {
            int primCount = type.ComputePrimitiveCount(Count);
            return new PrimitiveDrawCall<T>(
                type, Buffer, Offset, primCount
            );
        }

        public PrimitiveDrawCall<T> GetDrawCall (PrimitiveType type, short[] indices, int indexOffset, int indexCount) {
            int primCount = type.ComputePrimitiveCount(indexCount);
            return new PrimitiveDrawCall<T>(
                type, Buffer, Offset, Count, indices, indexOffset, primCount
            );
        }

        public PrimitiveDrawCall<T> GetDrawCall (PrimitiveType type, ref IndexWriter indices) {
            int primCount = type.ComputePrimitiveCount(indices.Count);
            return new PrimitiveDrawCall<T>(
                type, Buffer, Offset, Count, indices.Buffer, indices.Offset, primCount
            );
        }

        public PrimitiveDrawCall<T> GetDrawCallTriangleFan (Batch batch) {
            int primCount = Count - 2;
            var ibuf = batch.Container.RenderManager.GetArrayAllocator<short>().Allocate(primCount * 3);
            var indices = ibuf.Buffer;

            for (int i = 2, j = 0; i < Count; i++, j += 3) {
                indices[j] = 0;
                indices[j + 1] = (short)(i - 1);
                indices[j + 2] = (short)i;
            }

            return new PrimitiveDrawCall<T>(
                PrimitiveType.TriangleList, Buffer, Offset, Count, indices, 0, primCount
            );
        }
    }

    public struct IndexBuffer : IDisposable {
        public readonly ArrayPoolAllocator<short>.Allocation Allocation;
        public int Count;

        public IndexBuffer(ArrayPoolAllocator<short> allocator, int capacity) {
            Allocation = allocator.Allocate(capacity);
            Count = 0;
        }

        public short[] Buffer {
            get {
                return Allocation.Buffer;
            }
        }

        public IndexWriter GetWriter(int capacity, short indexOffset) {
            var offset = Count;
            var newCount = Count + capacity;

            if (newCount >= Allocation.Buffer.Length)
                throw new InvalidOperationException();

            // FIXME: This shouldn't be needed!
            Array.Clear(this.Allocation.Buffer, offset, capacity);

            Count = newCount;
            return new IndexWriter(this.Allocation.Buffer, offset, capacity, indexOffset);
        }

        public void Dispose() {
            Count = 0;
        }
    }

    public struct IndexWriter {
        public readonly short[] Buffer;
        public readonly int Offset;
        public readonly int Size;
        public readonly short IndexOffset;
        public int Count;

        public IndexWriter (short[] buffer, int offset, int size, short indexOffset) {
            Buffer = buffer;
            Offset = offset;
            IndexOffset = indexOffset;
            Size = size;
            Count = 0;
        }

        public void Write (short newIndex) {
            if (Count >= Size)
                throw new InvalidOperationException();

            Buffer[Offset + Count] = (short)(newIndex + IndexOffset);
            Count += 1;
        }

        public void Write (short[] newIndices) {
            int l = newIndices.Length;
            if (Count + l - 1 >= Size)
                throw new InvalidOperationException();

            for (int i = 0; i < l; i++)
                Buffer[Offset + Count + i] = (short)(newIndices[i] + IndexOffset);

            Count += l;
        }

        public void Write (int index, short newIndex) {
            if (index >= Size)
                throw new InvalidOperationException();

            Count = Math.Max(Count, index + 1);
            index += Offset;

            Buffer[index] = (short)(newIndex + IndexOffset);
        }
    }
}

namespace Squared.Render {
    public class PrimitiveBatch<T> : ListBatch<PrimitiveDrawCall<T>>
        where T : struct, IVertexType {

        private ArrayPoolAllocator<T> _Allocator;
        private Action<DeviceManager, object> _BatchSetup;
        private object _UserData;

        public void Initialize (IBatchContainer container, int layer, Material material, Action<DeviceManager, object> batchSetup, object userData) {
            base.Initialize(container, layer, material);

            if (_Allocator == null)
                _Allocator = container.RenderManager.GetArrayAllocator<T>();

            _BatchSetup = batchSetup;
            _UserData = userData;
        }

        public Internal.VertexBuffer<T> CreateBuffer (int capacity) {
            return new Internal.VertexBuffer<T>(_Allocator, capacity);
        }

        public void Add (PrimitiveDrawCall<T> item) {
            Add(ref item);
        }

        new public void Add (ref PrimitiveDrawCall<T> item) {
            if (item.Vertices == null)
                return;

#if VALIDATE
            var indexCount = item.PrimitiveType.ComputeVertexCount(item.PrimitiveCount);
            Debug.Assert(
                item.Indices.Length >= item.IndexOffset + indexCount
            );

            for (int i = 0; i < indexCount; i++) {
                Debug.Assert(item.Indices[i + item.IndexOffset] >= 0);
                Debug.Assert(item.Indices[i + item.IndexOffset] < item.VertexCount);
            }
#endif

            int count = _DrawCalls.Count;
            while (count > 0) {
                PrimitiveDrawCall<T> lastCall = _DrawCalls[count - 1];

                // Attempt to combine
                if (lastCall.PrimitiveType != item.PrimitiveType)
                    break;

                if ((item.PrimitiveType == PrimitiveType.TriangleStrip) || (item.PrimitiveType == PrimitiveType.LineStrip))
                    break;

                if (lastCall.Vertices != item.Vertices)
                    break;
                if (item.VertexOffset != lastCall.VertexOffset + lastCall.VertexCount)
                    break;

                if ((lastCall.Indices ?? item.Indices) != null)
                    break;

                _DrawCalls[count - 1] = new PrimitiveDrawCall<T>(
                    lastCall.PrimitiveType, lastCall.Vertices, 
                    lastCall.VertexOffset, lastCall.VertexCount + item.VertexCount, 
                    null, 0, 
                    lastCall.PrimitiveCount + item.PrimitiveCount
                );
                return;
            }

            base.Add(ref item);
        }

        public override void Prepare () {
        }

        public override void Issue (DeviceManager manager) {
            if (_DrawCalls.Count == 0)
                return;

            if (_BatchSetup != null)
                _BatchSetup(manager, _UserData);

            using (manager.ApplyMaterial(Material)) {
                var device = manager.Device;

                foreach (var call in _DrawCalls) {
                    if (call.Indices != null) {
                        device.DrawUserIndexedPrimitives<T>(
                            call.PrimitiveType, call.Vertices, call.VertexOffset, call.VertexCount, call.Indices, call.IndexOffset, call.PrimitiveCount
                        );
                    } else {
                        device.DrawUserPrimitives<T>(
                            call.PrimitiveType, call.Vertices, call.VertexOffset, call.PrimitiveCount
                        );
                    }
                }
            }
        }

        public static PrimitiveBatch<T> New (IBatchContainer container, int layer, Material material, Action<DeviceManager, object> batchSetup = null, object userData = null) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = container.RenderManager.AllocateBatch<PrimitiveBatch<T>>();
            result.Initialize(container, layer, material, batchSetup, userData);
            return result;
        }
    }

    public static class PrimitiveDrawCall {
        public static PrimitiveDrawCall<T> New<T> (PrimitiveType primitiveType, T[] vertices)
            where T : struct {

            return New<T>(primitiveType, vertices, 0, primitiveType.ComputePrimitiveCount(vertices.Length));
        }

        public static PrimitiveDrawCall<T> New<T> (PrimitiveType primitiveType, T[] vertices, int vertexOffset, int primitiveCount)
            where T : struct {

            return new PrimitiveDrawCall<T>(
                primitiveType,
                vertices,
                vertexOffset,
                primitiveCount
            );
        }

        public static PrimitiveDrawCall<T> New<T> (PrimitiveType primitiveType, T[] vertices, int vertexOffset, int vertexCount, short[] indices, int indexOffset, int primitiveCount)
            where T : struct {

            return new PrimitiveDrawCall<T>(
                primitiveType,
                vertices,
                vertexOffset,
                vertexCount,
                indices,
                indexOffset,
                primitiveCount
            );
        }
    }

    public struct PrimitiveDrawCall<T> 
        where T : struct {

        public readonly PrimitiveType PrimitiveType;
        public readonly short[] Indices;
        public readonly int IndexOffset;
        public readonly T[] Vertices;
        public readonly int VertexOffset;
        public readonly int VertexCount;
        public readonly int PrimitiveCount;

        public static PrimitiveDrawCall<T> Null = new PrimitiveDrawCall<T>();

        public PrimitiveDrawCall (PrimitiveType primitiveType, T[] vertices, int vertexOffset, int primitiveCount)
            : this (primitiveType, vertices, vertexOffset, vertices.Length, null, 0, primitiveCount) {
        }

        public PrimitiveDrawCall (PrimitiveType primitiveType, T[] vertices, int vertexOffset, int vertexCount, short[] indices, int indexOffset, int primitiveCount) {
            if (primitiveCount <= 0)
                throw new ArgumentOutOfRangeException("primitiveCount", "At least one primitive must be drawn within a draw call.");
            if (vertexCount <= 0)
                throw new ArgumentOutOfRangeException("vertexCount", "At least one vertex must be provided.");

            PrimitiveType = primitiveType;
            Vertices = vertices;
            VertexOffset = vertexOffset;
            VertexCount = vertexCount;
            Indices = indices;
            IndexOffset = indexOffset;
            PrimitiveCount = primitiveCount;
        }
    }

    public class NativeBatch : ListBatch<NativeDrawCall> {
        private Action<DeviceManager, object> _BatchSetup;
        private object _UserData;

        public void Initialize (IBatchContainer container, int layer, Material material, Action<DeviceManager, object> batchSetup, object userData) {
            base.Initialize(container, layer, material);

            _BatchSetup = batchSetup;
            _UserData = userData;
        }

        public void Add (NativeDrawCall item) {
            Add(ref item);
        }

        new public void Add (ref NativeDrawCall item) {
            base.Add(ref item);
        }

        public override void Prepare () {
        }

        public override void Issue (DeviceManager manager) {
            if (_DrawCalls.Count == 0)
                return;

            if (_BatchSetup != null)
                _BatchSetup(manager, _UserData);

            using (manager.ApplyMaterial(Material)) {
                var device = manager.Device;

                foreach (var call in _DrawCalls) {
                    device.SetVertexBuffer(call.VertexBuffer, call.VertexOffset);
                    device.Indices = call.IndexBuffer;

                    if (call.IndexBuffer != null)
                        device.DrawIndexedPrimitives(call.PrimitiveType, call.BaseVertex, call.MinVertexIndex, call.NumVertices, call.StartIndex, call.PrimitiveCount);
                    else
                        device.DrawPrimitives(call.PrimitiveType, call.StartVertex, call.PrimitiveCount);

                }

                device.SetVertexBuffer(null);
                device.Indices = null;
            }
        }

        public static NativeBatch New (IBatchContainer container, int layer, Material material, Action<DeviceManager, object> batchSetup = null, object userData = null) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = container.RenderManager.AllocateBatch<NativeBatch>();
            result.Initialize(container, layer, material, batchSetup, userData);
            return result;
        }
    }

    public struct NativeDrawCall {
        public readonly PrimitiveType PrimitiveType;
        public readonly VertexBuffer VertexBuffer;
        public readonly IndexBuffer IndexBuffer;

        public readonly int VertexOffset;
        public readonly int BaseVertex;
        public readonly int MinVertexIndex;
        public readonly int NumVertices;
        public readonly int StartIndex;
        public readonly int StartVertex;
        public readonly int PrimitiveCount;

        public NativeDrawCall (PrimitiveType primitiveType, VertexBuffer vertexBuffer, int vertexOffset, IndexBuffer indexBuffer, int baseVertex, int minVertexIndex, int numVertices, int startIndex, int primitiveCount) {
            if (vertexBuffer == null)
                throw new ArgumentNullException("vertexBuffer");

            PrimitiveType = primitiveType;
            VertexBuffer = vertexBuffer;
            VertexOffset = vertexOffset;
            IndexBuffer = indexBuffer;
            BaseVertex = baseVertex;
            MinVertexIndex = minVertexIndex;
            NumVertices = numVertices;
            StartIndex = startIndex;
            PrimitiveCount = primitiveCount;

            StartVertex = 0;
        }

        public NativeDrawCall (PrimitiveType primitiveType, VertexBuffer vertexBuffer, int vertexOffset, int startVertex, int primitiveCount) {
            if (vertexBuffer == null)
                throw new ArgumentNullException("vertexBuffer");

            PrimitiveType = primitiveType;
            VertexBuffer = vertexBuffer;
            VertexOffset = vertexOffset;
            StartVertex = startVertex;
            PrimitiveCount = primitiveCount;

            IndexBuffer = null;
            BaseVertex = MinVertexIndex = NumVertices = StartIndex = 0;
        }
    }

    public static class Primitives {
        public static void FilledArc (PrimitiveBatch<VertexPositionColor> batch, Vector2 center, float innerRadius, float outerRadius, float startAngle, float endAngle, Color startColor, Color endColor) {
            FilledArc(batch, center, new Vector2(innerRadius, innerRadius), new Vector2(outerRadius, outerRadius), startAngle, endAngle, startColor, endColor);
        }

        public static void FilledArc (PrimitiveBatch<VertexPositionColor> batch, Vector2 center, Vector2 innerRadius, Vector2 outerRadius, float startAngle, float endAngle, Color startColor, Color endColor) {
            if (endAngle <= startAngle)
                return;

            int numPoints = (int)Math.Ceiling(Math.Abs(endAngle - startAngle) * 6) * 2 + 4;
            float a = startAngle, c = 0.0f;
            float astep = (float)((endAngle - startAngle) / (numPoints - 2) * 2), cstep = 1.0f / (numPoints - 2) * 2;
            float cos, sin;
            var vertex = new VertexPositionColor();

            using (var buffer = batch.CreateBuffer(numPoints + 2)) {
                var points = buffer.Buffer;

                for (int i = 0; i < numPoints; i++) {
                    cos = (float)Math.Cos(a);
                    sin = (float)Math.Sin(a);

                    vertex.Color = Color.Lerp(startColor, endColor, c);
                    vertex.Position.X = center.X + (float)(cos * innerRadius.X);
                    vertex.Position.Y = center.Y + (float)(sin * innerRadius.Y);
                    points[i] = vertex;

                    i++;

                    vertex.Position.X = center.X + (float)(cos * outerRadius.X);
                    vertex.Position.Y = center.Y + (float)(sin * outerRadius.Y);
                    points[i] = vertex;

                    a += astep;
                    if (a > endAngle)
                        a = endAngle;
                    c += cstep;
                }

                batch.Add(PrimitiveDrawCall.New(
                    PrimitiveType.TriangleStrip,
                    points, 0, numPoints - 2
                ));
            }
        }
    }
}
