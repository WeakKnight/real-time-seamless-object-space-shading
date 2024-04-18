using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor.AssetImporters;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

[ScriptedImporter(1, "htx")]
public class HtexImporter : ScriptedImporter
{
    public unsafe override void OnImportAsset(AssetImportContext ctx)
    {
        IntPtr pTexture = Htex.Bindings.HtexTexture_open(ctx.assetPath);
        try
        {
            ImportTextre(ctx, pTexture);
        }
        finally
        {
            Htex.Bindings.HtexTexture_release(pTexture);
        }
    }

    unsafe void ImportTextre(AssetImportContext ctx, IntPtr pHtexTexture)
    {
        Htex.Info texInfo = Htex.Bindings.HtexTexture_getInfo(pHtexTexture);

        // Assume all quad textures are the same size
        int quadWidth = 0, quadHeight = 0;
        Htex.Bindings.HtexTexture_getQuadResolution(pHtexTexture, 0, ref quadWidth, ref quadHeight);

        Debug.Assert(texInfo.numChannels == 4);
        Debug.Assert(texInfo.dataType == Htex.DataType.dt_uint8);

        int numQuadsX = Mathf.Min(texInfo.numFaces, 128);
        int numQuadsY = (texInfo.numFaces + numQuadsX - 1) / numQuadsX;
        byte[] textureData = new byte[quadWidth * numQuadsX * quadHeight * numQuadsY * 4];
        Parallel.For(0, texInfo.numFaces, quadID =>
        {
            int tileX = quadID % numQuadsX;
            int tileY = quadID / numQuadsX;

            NativeArray<byte> quadData = new NativeArray<byte>(quadWidth * quadHeight * 4, Allocator.TempJob);
            IntPtr pData = (IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(quadData);
            Htex.Bindings.HtexTexture_getData(pHtexTexture, quadID, pData, 0);

            for (int y = 0; y < quadHeight; ++y)
            {
                for (int x = 0; x < quadWidth; ++x)
                {
                    int srcIdx = y * quadWidth + x;
                    int dstIdx = ((tileY * quadHeight + y) * numQuadsX * quadWidth + tileX * quadWidth + x);
                    textureData[dstIdx * 4 + 0] = quadData[srcIdx * 4 + 0];
                    textureData[dstIdx * 4 + 1] = quadData[srcIdx * 4 + 1];
                    textureData[dstIdx * 4 + 2] = quadData[srcIdx * 4 + 2];
                    textureData[dstIdx * 4 + 3] = quadData[srcIdx * 4 + 3];
                }
            }
            quadData.Dispose();
        });
        
        Texture2D texture = new Texture2D(quadWidth * numQuadsX, quadHeight * numQuadsY, TextureFormat.RGBA32, false);
        texture.name = "HtexTextureAtlas";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixelData(textureData, 0, 0);
        texture.Apply();
        ctx.AddObjectToAsset("HtexTextureAtlas", texture);

        Htex.TextureInfo textureInfo = ScriptableObject.CreateInstance<Htex.TextureInfo>();
        textureInfo.name = "Info";
        textureInfo.numFaces = texInfo.numFaces;
        textureInfo.quadSize = new Vector2Int(quadWidth, quadHeight);
        textureInfo.numQuads = new Vector2Int(numQuadsX, numQuadsY);

        ctx.AddObjectToAsset("Info", textureInfo);

        ctx.SetMainObject(texture);
    }
}
