using System;
using System.Collections.Generic;
using Seb.Vis.Internal;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Vis
{
	// (Partial) Draw class: shapes that stay on the GPU between frames.
	//
	// Normally every shape is added to a list each frame and the whole list is uploaded.
	// For big scenes that do not change, the shapes can instead be captured once into a
	// CachedShapeBuffer, which keeps them in a GPU buffer and only uploads the parts that
	// are patched. Its Draw() inserts the buffer at the start of the current layer.
	public static partial class Draw
	{
		public enum ShapeSink
		{
			Draw, // the normal way: shapes are drawn this frame
			Capture, // shapes are appended to a list instead (text is dropped)
			Discard // shapes are dropped (text is still drawn)
		}

		static ShapeSink shapeSink = ShapeSink.Draw;
		static List<ShapeData> captureList;

		// Where shapes from the drawing functions go until the next call. Capture needs the list
		// to append to. Reserve and Modify functions always draw.
		public static void SetShapeSink(ShapeSink sink, List<ShapeData> captureInto = null)
		{
			if (sink == ShapeSink.Capture && captureInto == null) throw new ArgumentNullException(nameof(captureInto));
			shapeSink = sink;
			captureList = sink == ShapeSink.Capture ? captureInto : null;
		}

		public static ShapeSink ActiveShapeSink => shapeSink;

		static void AddShape(ShapeData data)
		{
			if (shapeSink == ShapeSink.Draw) shapeDrawer.AddToLayer(data);
			else if (shapeSink == ShapeSink.Capture) captureList.Add(data);
		}

		// Whether cached shapes can be drawn on this platform (they need structured buffers in shaders).
		public static bool SupportsCachedShapes => !UseMeshRendering;

		// Draws registered for a layer, run before the layer's shapes (index = order the layers were started in).
		static readonly List<(int layerIndex, LayerInfo layer, CachedShapeBuffer buffer)> cachedDraws = new();
		static int cachedDrawFrame = -1;

		internal static void AddCachedDraw(CachedShapeBuffer buffer)
		{
			if (!isInitSinceCleanup) Init();
			if (layers.Count == 0 || shapeDrawer.LayerCount == 0) throw new Exception("Layer has not yet been started.");

			// A frame that was never dispatched (no camera rendered) must not leave its draws behind.
			if (cachedDrawFrame != Time.frameCount)
			{
				cachedDraws.Clear();
				cachedDrawFrame = Time.frameCount;
			}

			// Same index as the shape drawer gives the layer this frame.
			cachedDraws.Add((shapeDrawer.LayerCount - 1, layers.Peek(), buffer));
		}

		static void DrawCachedForLayer(CommandBuffer commandBuffer, int layerIndex)
		{
			foreach ((int index, LayerInfo layer, CachedShapeBuffer buffer) in cachedDraws)
			{
				if (index == layerIndex) buffer.Record(commandBuffer, layer, quadMesh);
			}
		}
	}

	// Shapes kept in a GPU buffer between frames. Not available with mesh rendering (WebGL).
	public sealed class CachedShapeBuffer : IDisposable
	{
		static readonly int instanceDataID = Shader.PropertyToID("InstanceData");
		static readonly int instanceOffsetID = Shader.PropertyToID("InstanceOffset");
		static readonly int layerOffsetID = Shader.PropertyToID("LayerOffset");
		static readonly int layerScaleID = Shader.PropertyToID("LayerScale");
		static readonly int screenSpaceID = Shader.PropertyToID("useScreenSpace");

		readonly uint[] args = new uint[5];
		ShapeData[] shapes = Array.Empty<ShapeData>();
		int count;
		ComputeBuffer shapeBuffer;
		ComputeBuffer argsBuffer;
		Material material;
		bool uploadAll;
		int dirtyStart = int.MaxValue;
		int dirtyEnd = -1;

		public int Count => count;

		// Replaces all shapes (uploaded in full before the next draw).
		public void SetShapes(List<ShapeData> newShapes)
		{
			count = newShapes.Count;
			if (shapes.Length < count) shapes = new ShapeData[Mathf.NextPowerOfTwo(Mathf.Max(count, 1))];
			newShapes.CopyTo(shapes);
			uploadAll = true;
			dirtyStart = int.MaxValue;
			dirtyEnd = -1;
		}

		// Overwrites shapes from start on; only that range is uploaded.
		public void Patch(int start, List<ShapeData> replacement)
		{
			if (start < 0 || start + replacement.Count > count) throw new ArgumentOutOfRangeException(nameof(start));
			replacement.CopyTo(0, shapes, start, replacement.Count);
			dirtyStart = Mathf.Min(dirtyStart, start);
			dirtyEnd = Mathf.Max(dirtyEnd, start + replacement.Count);
		}

		// Draws the shapes at the start of the current layer, under everything else drawn in it.
		public void Draw()
		{
			if (count > 0) Seb.Vis.Draw.AddCachedDraw(this);
		}

		internal void Record(CommandBuffer cmd, Seb.Vis.Draw.LayerInfo layer, Mesh quad)
		{
			if (count == 0 || quad == null) return;

			int stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ShapeData));
			if (shapeBuffer == null || !shapeBuffer.IsValid() || shapeBuffer.count < count)
			{
				shapeBuffer?.Release();
				shapeBuffer = new ComputeBuffer(shapes.Length, stride);
				uploadAll = true;
			}

			if (uploadAll)
			{
				shapeBuffer.SetData(shapes, 0, 0, count);
			}
			else if (dirtyEnd > dirtyStart)
			{
				shapeBuffer.SetData(shapes, dirtyStart, dirtyStart, dirtyEnd - dirtyStart);
			}

			uploadAll = false;
			dirtyStart = int.MaxValue;
			dirtyEnd = -1;

			argsBuffer ??= new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
			args[0] = quad.GetIndexCount(0);
			args[1] = (uint)count;
			args[2] = quad.GetIndexStart(0);
			args[3] = quad.GetBaseVertex(0);
			args[4] = 0;
			argsBuffer.SetData(args);

			if (material == null) material = new Material(Shader.Find("Vis/Draw"));
			material.SetBuffer(instanceDataID, shapeBuffer);
			material.SetInt(instanceOffsetID, 0);
			material.SetVector(layerOffsetID, layer.offset);
			material.SetFloat(layerScaleID, layer.scale);
			material.SetInt(screenSpaceID, layer.useScreenSpace ? 1 : 0);

			cmd.DrawMeshInstancedIndirect(quad, 0, material, 0, argsBuffer, 0);
		}

		public void Dispose()
		{
			shapeBuffer?.Release();
			argsBuffer?.Release();
			shapeBuffer = null;
			argsBuffer = null;
			if (material != null)
			{
				if (Application.isPlaying) UnityEngine.Object.Destroy(material);
				else UnityEngine.Object.DestroyImmediate(material);
				material = null;
			}

			count = 0;
		}
	}
}
