using System.Collections.Generic;
using Seb.Vis.Text.FontLoading;
using UnityEngine;

namespace Seb.Vis.Text.Rendering
{
	public class TextRenderData
	{
		// [Shader Data] for each glyph, stores: bezier points
		public readonly List<Vector2> bezierPoints = new();

		// [Shader Data] for each unique glyph, stores: bezier offset, num contours, contour length/s
		public readonly List<int> glyphMetadata = new();
		public readonly Dictionary<Glyph, int> glyphMetadataIndexLookup = new();

		// The same data as float textures, one value per texel, for drawing without structured
		// buffers (Draw.UseMeshRendering). Both lists only grow, so the textures are rebuilt when
		// something was added.
		public const int DataTextureWidth = 1024;
		Texture2D bezierTexture;
		Texture2D metadataTexture;
		int bezierCountInTexture = -1;
		int metadataCountInTexture = -1;

		public Texture2D BezierTexture
		{
			get
			{
				if (bezierCountInTexture != bezierPoints.Count || bezierTexture == null)
				{
					bezierCountInTexture = bezierPoints.Count;
					bezierTexture = FillTexture(bezierTexture, bezierPoints.Count, (data, i) =>
					{
						data[i * 4] = bezierPoints[i].x;
						data[i * 4 + 1] = bezierPoints[i].y;
					});
				}

				return bezierTexture;
			}
		}

		public Texture2D MetadataTexture
		{
			get
			{
				if (metadataCountInTexture != glyphMetadata.Count || metadataTexture == null)
				{
					metadataCountInTexture = glyphMetadata.Count;
					metadataTexture = FillTexture(metadataTexture, glyphMetadata.Count, (data, i) => data[i * 4] = glyphMetadata[i]);
				}

				return metadataTexture;
			}
		}

		static Texture2D FillTexture(Texture2D texture, int count, System.Action<float[], int> write)
		{
			int height = Mathf.Max(1, (count + DataTextureWidth - 1) / DataTextureWidth);
			if (texture == null || texture.height != height)
			{
				if (texture != null)
				{
					if (Application.isPlaying) Object.Destroy(texture);
					else Object.DestroyImmediate(texture);
				}
				texture = new Texture2D(DataTextureWidth, height, TextureFormat.RGBAFloat, false, true)
				{
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};
			}

			float[] data = new float[DataTextureWidth * height * 4];
			for (int i = 0; i < count; i++) write(data, i);
			texture.SetPixelData(data, 0);
			texture.Apply(false, false);
			return texture;
		}
	}
}