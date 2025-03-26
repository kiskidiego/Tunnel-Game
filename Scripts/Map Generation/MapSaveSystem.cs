using Godot;
using System.IO.Compression;
using System.IO;
using FileAccess = Godot.FileAccess;
using System.Text;
using System.Linq;

public partial class MapSaveSystem
{
	static string path = "res://maps/";
	static Map map;
	public static void CreateMap(string name, Vector3I size)
	{
		map = new Map();
		map.name = name;
		map.size = size;
		map.chunks = new MapChunk[size.X, size.Y, size.Z];
		for(int x = 0; x < size.X; x++)
		{
			for (int y = 0; y < size.Y; y++)
			{
				for (int z = 0; z < size.Z; z++)
				{
					map.chunks[x, y, z].index = new Vector3I(x, y, z);
				}
			}
		}
	}
	public static void AddCurvePoints(Vector3I chunkIndex, MapCurvePoint[] curvePoints)
	{
		map.chunks[chunkIndex.X, chunkIndex.Y, chunkIndex.Z].curvePoints = curvePoints;
	}
	public static void AddNodes(Vector3I chunkIndex, MapNode node)
	{
		if(map.chunks[chunkIndex.X, chunkIndex.Y, chunkIndex.Z].nodes == null)
		{
			map.chunks[chunkIndex.X, chunkIndex.Y, chunkIndex.Z].nodes = new MapNode[0];
		}
		map.chunks[chunkIndex.X, chunkIndex.Y, chunkIndex.Z].nodes.Append(node);
	}
	public static void SaveMap()
	{
		DirAccess.MakeDirRecursiveAbsolute(path);
		var file = FileAccess.Open(path + map.name + ".tgm", FileAccess.ModeFlags.Write);
		string data = map.name + "\n" + map.size + "\n";
		foreach (var chunk in map.chunks)
		{
			if (chunk.curvePoints == null && chunk.nodes == null) continue;
			data += "#" + chunk.index;
			if (chunk.curvePoints != null)
			{
				data += "C";
				foreach (var curvePoint in chunk.curvePoints)
				{
					data += curvePoint.position + " " + curvePoint.radius + "\n";
				}
			}
			
			if (chunk.nodes != null)
			{
				data += "N";
				foreach (var node in chunk.nodes)
				{
					data += node.position + " " + node.score + "\n";
				}
			}
		}
		byte[] compressedData = Compress(Encoding.UTF8.GetBytes(data));
		file.StoreBuffer(compressedData);
		file.Close();
	}
	public static Map LoadMap(string name)
	{
		var file = FileAccess.Open(path + name + ".tgm", FileAccess.ModeFlags.Read);
		byte[] compressedData = file.GetBuffer((long)file.GetLength());
		byte[] data = Decompress(compressedData);
		file.Close();
		map = (Map)GD.BytesToVar(data);
		return map;
	}

	static byte[] Compress(byte[] data)
	{
		using (var compressedStream = new MemoryStream())
		using (var zipStream = new GZipStream(compressedStream, CompressionMode.Compress))
		{
			zipStream.Write(data, 0, data.Length);
			return compressedStream.ToArray();
		}
	}

	static byte[] Decompress(byte[] data)
	{
		using (var compressedStream = new MemoryStream(data))
		using (var zipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
		using (var resultStream = new MemoryStream())
		{
			zipStream.CopyTo(resultStream);
			return resultStream.ToArray();
		}
	}

}
public partial class Map : GodotObject
{
	public string name;
	public Vector3I size;
	public MapChunk[,,] chunks;
}
public struct MapChunk
{
	public Vector3I index;
	public MapCurvePoint[] curvePoints;
	public MapNode[] nodes;
}
public struct MapNode
{
	public Vector3 position;
	public float score;
}
public struct MapCurvePoint
{
	public Vector3 position;
	public float radius;
}