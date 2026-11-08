using System;
using System.Collections.Generic;
using Seb.Types;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Vis.Internal
{
	// Draws shapes with indirect instancing from a structured buffer, or, where shaders cannot read
	// structured buffers (Draw.UseMeshRendering, e.g. WebGL), as an ordinary mesh built each frame:
	// four vertices per shape, each carrying the shape's data (see DrawMesh.shader).
	public class ShapeDrawer : InstancedDrawer<ShapeData>
	{
		const string MeshShaderName = "Vis/DrawMesh";

		static readonly int layerOffsetID = Shader.PropertyToID("LayerOffset");
		static readonly int layerScaleID = Shader.PropertyToID("LayerScale");
		static readonly int screenSpaceID = Shader.PropertyToID("useScreenSpace");

		static readonly Vector3[] corners =
		{
			new(-0.5f, 0.5f),
			new(0.5f, 0.5f),
			new(-0.5f, -0.5f),
			new(0.5f, -0.5f)
		};

		static readonly Vector2[] cornerUVs =
		{
			new(0, 1),
			new(1, 1),
			new(0, 0),
			new(1, 0)
		};

		readonly Pool<Mesh> meshPool = new(CreateMesh);
		readonly Pool<DrawMaterial> meshMaterialPool;

		readonly List<Vector3> vertices = new();
		readonly List<Vector2> uvs = new();
		readonly List<Vector4> abData = new();
		readonly List<Vector4> typeParamData = new();
		readonly List<Vector4> colData = new();
		readonly List<Vector4> maskData = new();
		readonly List<int> indices = new();

		public ShapeDrawer(Mesh mesh, Shader shader) : base(mesh, shader)
		{
			Shader meshShader = Shader.Find(MeshShaderName);
			meshMaterialPool = new Pool<DrawMaterial>(() => new DrawMaterial(meshShader));
		}

		static Mesh CreateMesh()
		{
			Mesh mesh = new();
			mesh.MarkDynamic();
			return mesh;
		}

		protected override void InitFrame()
		{
			base.InitFrame();
			meshPool.ReturnAll();
			meshMaterialPool.ReturnAll();
		}

		protected override void DrawLayer(CommandBuffer cmd, int startIndex, int count, Draw.LayerInfo layerInfo)
		{
			if (!Draw.UseMeshRendering)
			{
				base.DrawLayer(cmd, startIndex, count, layerInfo);
				return;
			}

			vertices.Clear();
			uvs.Clear();
			abData.Clear();
			typeParamData.Clear();
			colData.Clear();
			maskData.Clear();
			indices.Clear();

			for (int i = startIndex; i < startIndex + count; i++)
			{
				ShapeData shape = allDrawData[i];
				Vector2 triangleC = shape.type == (int)ShapeType.Triangle ? UnpackFloats(shape.c) : Vector2.zero;
				Vector4 ab = new(shape.a.x, shape.a.y, shape.b.x, shape.b.y);
				Vector4 typeParam = new(shape.type, shape.c, triangleC.x, triangleC.y);
				Vector4 col = shape.col;
				Vector4 mask = new(shape.maskMin.x, shape.maskMin.y, shape.maskMax.x, shape.maskMax.y);

				int first = vertices.Count;
				for (int corner = 0; corner < 4; corner++)
				{
					vertices.Add(corners[corner]);
					uvs.Add(cornerUVs[corner]);
					abData.Add(ab);
					typeParamData.Add(typeParam);
					colData.Add(col);
					maskData.Add(mask);
				}

				indices.Add(first);
				indices.Add(first + 1);
				indices.Add(first + 2);
				indices.Add(first + 2);
				indices.Add(first + 1);
				indices.Add(first + 3);
			}

			Mesh layerMesh = meshPool.GetNextAvailableOrCreate();
			layerMesh.Clear();
			layerMesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
			layerMesh.SetVertices(vertices);
			layerMesh.SetUVs(0, uvs);
			layerMesh.SetUVs(1, abData);
			layerMesh.SetUVs(2, typeParamData);
			layerMesh.SetUVs(3, colData);
			layerMesh.SetUVs(4, maskData);
			layerMesh.SetTriangles(indices, 0, false);
			// Shapes are placed by the shader, so the mesh's own bounds mean nothing; keep it from being culled.
			layerMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);

			Material mat = meshMaterialPool.GetNextAvailableOrCreate().material;
			mat.SetVector(layerOffsetID, layerInfo.offset);
			mat.SetFloat(layerScaleID, layerInfo.scale);
			mat.SetInt(screenSpaceID, layerInfo.useScreenSpace ? 1 : 0);
			cmd.DrawMesh(layerMesh, Matrix4x4.identity, mat, 0, 0);
		}

		// The third corner of a triangle, stored in ShapeData.c as two halves.
		static Vector2 UnpackFloats(float packed)
		{
			uint bits = (uint)BitConverter.SingleToInt32Bits(packed);
			return new Vector2(Mathf.HalfToFloat((ushort)(bits >> 16)), Mathf.HalfToFloat((ushort)(bits & 0xFFFF)));
		}

		public override void Release()
		{
			base.Release();
			meshMaterialPool.ReturnAll();
			while (meshMaterialPool.HasAvailable()) DestroyObject(meshMaterialPool.PurgeNextAvailable().material);
			meshPool.ReturnAll();
			while (meshPool.HasAvailable()) DestroyObject(meshPool.PurgeNextAvailable());
		}

		static void DestroyObject(UnityEngine.Object obj)
		{
			if (obj == null) return;
			if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
			else UnityEngine.Object.DestroyImmediate(obj);
		}
	}
}
