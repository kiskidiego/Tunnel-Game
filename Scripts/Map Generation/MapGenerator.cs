using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Array = Godot.Collections.Array;

public partial class MapGenerator : Node
{
	//TODO: Do something about already rendered chunks (they occupy memory)
	class Point
	{
		public Vector3 position;
		public float score = -1;
		public short surfaceIndex = -1;
	}
	class Curve
	{
		public Vector3 startPoint;
		public Vector3 endPoint;
		public Vector3 controlPoint;
	}
	class CurvePoint
	{
		public Vector3 position;
		public short surfaceIndex;
		public CurvePoint(Vector3 position, short surfaceIndex = -1)
		{
			this.position = position;
			this.surfaceIndex = surfaceIndex;
		}
	}
	class Chunk
	{
		public bool loaded = false;
		public bool rendered = false;
		public Vector3I position;
		public Vector3I index;
		public Vector3 worldPosition;
		public Point[,,] grid;
		public List<CurvePoint> curvePoints = new List<CurvePoint>();
		public Mesh mesh;
		public MeshInstance3D meshInstance;
		public CollisionShape3D collisionShape;
		public Chunk aboveChunk;
		public Chunk belowChunk;
		public Chunk leftChunk;
		public Chunk rightChunk;
		public Chunk frontChunk;
		public Chunk backChunk;
		public Chunk(Vector3I position, Vector3I index, Vector3 worldPosition)
		{
			this.position = position;
			this.index = index;
			this.worldPosition = worldPosition;
		}
	}
	[Export] float worldWidth = 100;
	[Export] float worldDepth = 100;
	[Export] float worldHeight = 100;
	[Export] public float chunkSize { get; private set; } = 10;
	[Export] float cubeSize = 1;
	[Export] Vector3 tunnelOrigin;
	[Export] Vector3I initialChunk;
	[Export] float tunnelRange = 10;
	[Export] int tunnelAmount = 10;
	[Export] int curveSamples = 10;
	[Export] float surfaceValue = 5;
	[Export] float chunkRenderDistance = 10;
	List<Chunk> renderedChunks = new List<Chunk>();
	float sqrSurfaceValue;
	float squaredChunkRenderDistance;
	int surfaceCount = 0;
	Vector3I lowestChunk;
	Vector3I currentChunk;
	Curve[] curves;
	Chunk[,,] chunkGrid;

	public override void _Ready()
	{
		sqrSurfaceValue = surfaceValue * surfaceValue;
		squaredChunkRenderDistance = chunkRenderDistance * chunkRenderDistance;
		lowestChunk = new Vector3I((int)MathF.Ceiling(worldWidth / (2 * chunkSize)), (int)MathF.Ceiling(worldHeight / (2 * chunkSize)), (int)MathF.Ceiling(worldDepth / (2 * chunkSize)));
		//GD.Print("Lowest chunk: " + lowestChunk);
		currentChunk = initialChunk;
		Vector3I topChunkIndex = GetChunkIndex(new Vector3(worldWidth / 2, worldHeight / 2, worldDepth / 2));
		chunkGrid = new Chunk[topChunkIndex.X + 1, topChunkIndex.Y + 1, topChunkIndex.Z + 1];
		for(int i = 0; i < chunkGrid.GetLength(0); i++)
		{
			for(int j = 0; j < chunkGrid.GetLength(1); j++)
			{
				for(int k = 0; k < chunkGrid.GetLength(2); k++)
				{
					Vector3I chunkIndex = new Vector3I(i, j, k);
					Vector3I chunkPosition = IndexToChunk(chunkIndex);
					Vector3 chunkWorldPosition = new Vector3(chunkPosition.X * chunkSize + chunkSize / 2, chunkPosition.Y * chunkSize + chunkSize / 2, chunkPosition.Z * chunkSize + chunkSize / 2);

					chunkGrid[i, j, k] = new Chunk(chunkPosition, chunkIndex, chunkWorldPosition);

					if (i > 0)
					{
						chunkGrid[i, j, k].leftChunk = chunkGrid[i - 1, j, k];
						chunkGrid[i - 1, j, k].rightChunk = chunkGrid[i, j, k];
					}
					if (j > 0)
					{
						chunkGrid[i, j, k].belowChunk = chunkGrid[i, j - 1, k];
						chunkGrid[i, j - 1, k].aboveChunk = chunkGrid[i, j, k];
					}
					if (k > 0)
					{
						chunkGrid[i, j, k].backChunk = chunkGrid[i, j, k - 1];
						chunkGrid[i, j, k - 1].frontChunk = chunkGrid[i, j, k];
					}
				}
			}
		}
		////GD.Print("Top chunk index: " + topChunkIndex);
		PrepareCurves();
		SampleCurves();
		
		DoChunkOperations(tunnelOrigin);
	}

	public void DoChunkOperations(Vector3 worldPosition)
	{
		GD.Print("Do chunk operations");
		if (renderedChunks.Count == 0)
		{
			Vector3I chunkIndex = GetChunkIndex(worldPosition);
			ProcessChunk(chunkGrid[chunkIndex.X, chunkIndex.Y, chunkIndex.Z]);
			renderedChunks.Add(chunkGrid[chunkIndex.X, chunkIndex.Y, chunkIndex.Z]);
			chunkGrid[chunkIndex.X, chunkIndex.Y, chunkIndex.Z].rendered = true;
		}
		for (int i = 0; i < renderedChunks.Count; i++)
		{
			float sqrDistance = renderedChunks[i].worldPosition.DistanceSquaredTo(worldPosition);
			if (sqrDistance > squaredChunkRenderDistance && renderedChunks[i].rendered)
			{
				renderedChunks[i].rendered = false;
				renderedChunks.RemoveAt(i);
				i--;
				continue;
			}
			for (int j = 0; j < 6; j++)
			{
				Chunk chunk = null;
				switch (j)
				{
					case 0:
						chunk = renderedChunks[i].aboveChunk;
						break;
					case 1:
						chunk = renderedChunks[i].belowChunk;
						break;
					case 2:
						chunk = renderedChunks[i].leftChunk;
						break;
					case 3:
						chunk = renderedChunks[i].rightChunk;
						break;
					case 4:
						chunk = renderedChunks[i].frontChunk;
						break;
					case 5:
						chunk = renderedChunks[i].backChunk;
						break;
				}
				if (chunk == null)
				{
					continue;
				}
				if(!chunk.rendered)
				{
					renderedChunks.Add(chunk);
					chunk.rendered = true;
				}

				ProcessChunk(chunk);
			}
		}
	}

	void ProcessChunk(Chunk chunk)
	{
		if(chunk.loaded)
		{
			return;
		}
		chunk.meshInstance = new MeshInstance3D();
		AddChild(chunk.meshInstance);
		chunk.meshInstance.Mesh = new ArrayMesh();
		chunk.collisionShape = new CollisionShape3D();
		StaticBody3D chunkBody = new StaticBody3D();
		chunkBody.AddChild(chunk.collisionShape);
		chunk.meshInstance.AddChild(chunkBody);
		bool renderChunk;
		PrepareGrid(chunk);
		GD.Print("Grid prepared at chunk: " + chunk.position);
		renderChunk = AssignScores(chunk);
		GD.Print("Scores assigned at chunk: " + chunk.position);
		chunk.loaded = true;
		if (renderChunk)
		{
			MarchingCubesAlgorithm(chunk, sqrSurfaceValue);
			GD.Print("Marching cubes algorithm executed at chunk: " + chunk.position);
		}
		chunk.grid = null;
	}

	void PrepareCurves()
	{
		Queue<Vector3> tunnelBranchPoints = new Queue<Vector3>();
		curves = new Curve[tunnelAmount];
		curves[0] = new Curve();
		curves[0].startPoint = tunnelOrigin;
		float x;
		do
		{
			x = (float)GD.RandRange(-tunnelRange, tunnelRange);
		} while (x > worldWidth / 2 - 2 * sqrSurfaceValue || x < -worldWidth / 2 + 2 * sqrSurfaceValue);
		float y;
		do
		{
			y = (float)GD.RandRange(-tunnelRange, tunnelRange/4);
		} while (y > worldHeight / 2 - 2 * sqrSurfaceValue || y < -worldHeight / 2 + 2 * sqrSurfaceValue);
		float z;
		do
		{
			z = (float)GD.RandRange(-tunnelRange, tunnelRange);
		} while (z > worldDepth / 2 - 2 * sqrSurfaceValue || z < -worldDepth / 2 + 2 * sqrSurfaceValue);
		curves[0].endPoint = new Vector3(x, y, z);
		Vector3 midpoint = (curves[0].startPoint + curves[0].endPoint) / 2;
		do
		{
			x = (float)GD.RandRange(-tunnelRange + midpoint.X, tunnelRange + midpoint.X);
		} while (x > worldWidth / 2 - 2 * sqrSurfaceValue || x < -worldWidth / 2 + 2 * sqrSurfaceValue);
		do
		{
			y = (float)GD.RandRange(-tunnelRange + midpoint.Y, tunnelRange + midpoint.Y);
		} while (y > worldHeight / 2 - 2 * sqrSurfaceValue || y < -worldHeight / 2 + 2 * sqrSurfaceValue);
		do
		{
			z = (float)GD.RandRange(-tunnelRange + midpoint.Z, tunnelRange + midpoint.Z);
		} while (z > worldDepth / 2 - 2 * sqrSurfaceValue || z < -worldDepth / 2 + 2 * sqrSurfaceValue);
		curves[0].controlPoint = new Vector3(x, y, z);
		tunnelBranchPoints.Enqueue(curves[0].endPoint);
		for (int i = 1; i < curves.Length; i++)
		{
			while (GD.RandRange(0, 1) == 0)
			{
				Vector3 branchPoint = tunnelBranchPoints.Dequeue();
				tunnelBranchPoints.Enqueue(branchPoint);
			}
			curves[i] = new Curve();
			curves[i].startPoint = tunnelBranchPoints.Dequeue();
			do
			{
				x = (float)GD.RandRange(-tunnelRange + curves[i].startPoint.X, tunnelRange + curves[i].startPoint.X);
			} while (x > worldWidth / 2 - 2 * sqrSurfaceValue || x < -worldWidth / 2 + 2 * sqrSurfaceValue);
			do
			{
				y = (float)GD.RandRange(-tunnelRange + curves[i].startPoint.Y, tunnelRange + curves[i].startPoint.Y);
			} while (y > worldHeight / 2 - 2 * sqrSurfaceValue || y < -worldHeight / 2 + 2 * sqrSurfaceValue);
			do
			{
				z = (float)GD.RandRange(-tunnelRange + curves[i].startPoint.Z, tunnelRange + curves[i].startPoint.Z);
			} while (z > worldDepth / 2 - 2 * sqrSurfaceValue || z < -worldDepth / 2 + 2 * sqrSurfaceValue);
			curves[i].endPoint = new Vector3(x, y, z);
			midpoint = (curves[i].startPoint + curves[i].endPoint) / 2;
			do
			{
				x = (float)GD.RandRange(-tunnelRange + midpoint.X, tunnelRange + midpoint.X);
			} while (x > worldWidth / 2 - 2 * sqrSurfaceValue || x < -worldWidth / 2 + 2 * sqrSurfaceValue);
			do
			{
				y = (float)GD.RandRange(-tunnelRange + midpoint.Y, tunnelRange + midpoint.Y);
			} while (y > worldHeight / 2 - 2 * sqrSurfaceValue || y < -worldHeight / 2 + 2 * sqrSurfaceValue);
			do
			{
				z = (float)GD.RandRange(-tunnelRange + midpoint.Z, tunnelRange + midpoint.Z);
			} while (z > worldDepth / 2 - 2 * sqrSurfaceValue || z < -worldDepth / 2 + 2 * sqrSurfaceValue);
			curves[i].controlPoint = new Vector3(x, y, z);
			tunnelBranchPoints.Enqueue(curves[i].endPoint);
		}
		//GD.Print("Curves prepared");
	}

	void SampleCurves()
	{
		for (int curve = 0; curve < tunnelAmount; curve++)
		{
			for (short i = 0; i < curveSamples; i++)
			{

				float t = (float)i / curveSamples;
				Vector3 q0 = curves[curve].startPoint.Lerp(curves[curve].controlPoint, t);
				Vector3 q1 = curves[curve].controlPoint.Lerp(curves[curve].endPoint, t);
				Vector3 point = q0.Lerp(q1, t);
				Vector3I chunk = GetChunkIndex(point);
				////GD.Print("Curve: " + curve + " Point: " + point + " Chunk: " + chunk);

				if (chunkGrid[chunk.X, chunk.Y, chunk.Z].curvePoints == null)
				{
					chunkGrid[chunk.X, chunk.Y, chunk.Z].curvePoints = new List<CurvePoint>();
				}
				chunkGrid[chunk.X, chunk.Y, chunk.Z].curvePoints.Add(new CurvePoint(q0.Lerp(q1, t), i));

				if (i == surfaceCount)
				{
					surfaceCount++;
				}
			}
		}
		//GD.Print("Curves sampled");
	}

	void PrepareGrid(Chunk chunk)
	{
		chunk.grid = new Point[(int)(chunkSize / cubeSize) + 1, (int)(chunkSize / cubeSize) + 1, (int)(chunkSize / cubeSize) + 1];

		int startIndex1 = 0;
		int startIndex2 = 0;
		int startIndex3 = 0;
		int endIndex1 = chunk.grid.GetLength(0);
		int endIndex2 = chunk.grid.GetLength(1);
		int endIndex3 = chunk.grid.GetLength(2);

		float leftLimit = chunk.position.X * chunkSize;
		float bottomLimit = chunk.position.Y * chunkSize;
		float backLimit = chunk.position.Z * chunkSize;

		float xStep = leftLimit;
		float yStep = bottomLimit;
		float zStep = backLimit;

		if(chunk.leftChunk.grid != null)
		{
			startIndex1 = 1;
			for(int i = 0; i < chunk.grid.GetLength(1); i++)
			{
				for(int j = 0; j < chunk.grid.GetLength(2); j++)
				{
					chunk.grid[0, i, j] = chunk.leftChunk.grid[chunk.leftChunk.grid.GetLength(0) - 1, i, j];
				}
			}
		}

		if(chunk.belowChunk.grid != null)
		{
			startIndex2 = 1;
			for(int i = 0; i < chunk.grid.GetLength(0); i++)
			{
				for(int j = 0; j < chunk.grid.GetLength(2); j++)
				{
					chunk.grid[i, 0, j] = chunk.belowChunk.grid[i, chunk.belowChunk.grid.GetLength(1) - 1, j];
				}
			}
		}

		if(chunk.backChunk.grid != null)
		{
			startIndex3 = 1;
			for(int i = 0; i < chunk.grid.GetLength(0); i++)
			{
				for(int j = 0; j < chunk.grid.GetLength(1); j++)
				{
					chunk.grid[i, j, 0] = chunk.backChunk.grid[i, j, chunk.backChunk.grid.GetLength(2) - 1];
				}
			}
		}

		if(chunk.rightChunk.grid != null)
		{
			endIndex1 = chunk.grid.GetLength(0) - 1;
			for(int i = 0; i < chunk.grid.GetLength(1); i++)
			{
				for(int j = 0; j < chunk.grid.GetLength(2); j++)
				{
					chunk.grid[chunk.grid.GetLength(0) - 1, i, j] = chunk.rightChunk.grid[0, i, j];
				}
			}
		}

		if(chunk.aboveChunk.grid != null)
		{
			endIndex2 = chunk.grid.GetLength(1) - 1;
			for(int i = 0; i < chunk.grid.GetLength(0); i++)
			{
				for(int j = 0; j < chunk.grid.GetLength(2); j++)
				{
					chunk.grid[i, chunk.grid.GetLength(1) - 1, j] = chunk.aboveChunk.grid[i, 0, j];
				}
			}
		}

		if(chunk.frontChunk.grid != null)
		{
			endIndex3 = chunk.grid.GetLength(2) - 1;
			for(int i = 0; i < chunk.grid.GetLength(0); i++)
			{
				for(int j = 0; j < chunk.grid.GetLength(1); j++)
				{
					chunk.grid[i, j, chunk.grid.GetLength(2) - 1] = chunk.frontChunk.grid[i, j, 0];
				}
			}
		}

		for (int i = startIndex1; i < endIndex1; i++)
		{
			for (int j = startIndex2; j < endIndex2; j++)
			{
				for (int k = startIndex3; k < endIndex3; k++)
				{
					chunk.grid[i, j, k] = new Point();
					chunk.grid[i, j, k].position = new Vector3(xStep, yStep, zStep);
					zStep += cubeSize;
				}
				zStep = backLimit;
				yStep += cubeSize;
			}
			yStep = bottomLimit;
			xStep += cubeSize;
		}
		//GD.Print("Grid prepared at chunk: " + chunk);
	}
	
	bool AssignScores(Chunk chunk)
	{
		////GD.Print("StartX: " + startX + " StartY: " + startY + " StartZ: " + startZ + " EndX: " + endX + " EndY: " + endY + " EndZ: " + endZ);
		for (int i = 0; i < chunk.grid.GetLength(0); i++)
		{
			for (int j = 0; j < chunk.grid.GetLength(1); j++)
			{
				for (int k = 0; k < chunk.grid.GetLength(2); k++)
				{
					if (chunk.grid[i, j, k].score != -1)
					{
						continue;
					}
					if (AssessScore(chunk.grid[i, j, k], chunk) == -1)
					{
						return false;
					}
				}
			}
		}
		return true;
	}

	float AssessScore(Point point, Chunk chunk)
	{
		float score = float.MaxValue;
		short surfaceIndex = -1;

		int startIndex = -Mathf.CeilToInt(surfaceValue / chunkSize + 1);
		int endIndex = -startIndex;

		for (int i = startIndex; i <= endIndex; i++)
		{
			for(int j = startIndex; j <= endIndex; j++)
			{
				for(int k = startIndex; k <= endIndex; k++)
				{
					if(chunk.index.X + i < 0 || chunk.index.X + i >= chunkGrid.GetLength(0) || chunk.index.Y + j < 0 || chunk.index.Y + j >= chunkGrid.GetLength(1) || chunk.index.Z + k < 0 || chunk.index.Z + k >= chunkGrid.GetLength(2))
					{
						continue;
					}
					if (chunkGrid[chunk.index.X + i, chunk.index.Y + j, chunk.index.Z + k].curvePoints == null)
					{
						continue;
					}
					for (int l = 0; l < chunkGrid[chunk.index.X + i, chunk.index.Y + j, chunk.index.Z + k].curvePoints.Count; l++)
					{
						float sqrDistance = (point.position - chunkGrid[chunk.index.X + i, chunk.index.Y + j, chunk.index.Z + k].curvePoints[l].position).LengthSquared();
						if (sqrDistance < score)
						{
							score = sqrDistance;
							surfaceIndex = chunkGrid[chunk.index.X + i, chunk.index.Y + j, chunk.index.Z + k].curvePoints[l].surfaceIndex;
						}
					}
				}
			}
		}
		if(score == float.MaxValue)
		{
			score = -1;
		}
		//GD.Print("Chunk: " + chunk.index + " Point: " + point.position + " Score: " + score);
		point.score = score;
		point.surfaceIndex = surfaceIndex;
		return score;
	}

	void MarchingCubesAlgorithm(Chunk chunk, float surfaceValue)
	{
		List<Vector3>[] vertices = new List<Vector3>[surfaceCount];
		List<Vector3>[] normals = new List<Vector3>[surfaceCount];

		for (int i = 0; i < chunk.grid.GetLength(0) - 1; i++)
		{
			for (int j = 0; j < chunk.grid.GetLength(1) - 1; j++)
			{
				for (int k = 0; k < chunk.grid.GetLength(2) - 1; k++)
				{
					byte cubeIndex = 0;
					if (chunk.grid[i, j, k].score < surfaceValue) 
						cubeIndex |= 1;
					if (chunk.grid[i + 1, j, k].score < surfaceValue) 
						cubeIndex |= 2;
					if (chunk.grid[i + 1, j, k + 1].score < surfaceValue) 
						cubeIndex |= 4;
					if (chunk.grid[i, j, k + 1].score < surfaceValue) 
						cubeIndex |= 8;
					if (chunk.grid[i, j + 1, k].score < surfaceValue) 
						cubeIndex |= 16;
					if (chunk.grid[i + 1, j + 1, k].score < surfaceValue) 
						cubeIndex |= 32;
					if (chunk.grid[i + 1, j + 1, k + 1].score < surfaceValue) 
						cubeIndex |= 64;
					if (chunk.grid[i, j + 1, k + 1].score < surfaceValue) 
						cubeIndex |= 128;

					if (cubeIndex == 0 || cubeIndex == 255) 
						continue;

					//GD.Print("Cube index: " + cubeIndex);
					
					int surfaceIndex = chunk.grid[i, j, k].surfaceIndex;

					if (vertices[surfaceIndex] == null)
					{
						vertices[surfaceIndex] = new List<Vector3>();
						normals[surfaceIndex] = new List<Vector3>();
					}

					Vector3[] edgeVertices = new Vector3[12];
					if ((MarchTables.edges[cubeIndex] & 1) == 1)
					{
						edgeVertices[0] = VertexInterpolation(chunk.grid[i, j, k].position, chunk.grid[i + 1, j, k].position, chunk.grid[i, j, k].score, chunk.grid[i + 1, j, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 2) == 2)
					{
						edgeVertices[1] = VertexInterpolation(chunk.grid[i + 1, j, k].position, chunk.grid[i + 1, j, k + 1].position, chunk.grid[i + 1, j, k].score, chunk.grid[i + 1, j, k + 1].score);
					}
					if ((MarchTables.edges[cubeIndex] & 4) == 4)
					{
						edgeVertices[2] = VertexInterpolation(chunk.grid[i + 1, j, k + 1].position, chunk.grid[i , j, k + 1].position, chunk.grid[i + 1, j, k + 1].score, chunk.grid[i, j, k + 1].score);
					}
					if ((MarchTables.edges[cubeIndex] & 8) == 8)
					{
						edgeVertices[3] = VertexInterpolation(chunk.grid[i, j, k + 1].position, chunk.grid[i, j, k].position, chunk.grid[i, j, k + 1].score, chunk.grid[i, j, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 16) == 16)
					{
						edgeVertices[4] = VertexInterpolation(chunk.grid[i, j + 1, k].position, chunk.grid[i + 1, j + 1, k].position, chunk.grid[i, j + 1, k].score, chunk.grid[i + 1, j + 1, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 32) == 32)
					{
						edgeVertices[5] = VertexInterpolation(chunk.grid[i + 1, j + 1, k].position, chunk.grid[i + 1, j + 1, k + 1].position, chunk.grid[i + 1, j + 1, k].score, chunk.grid[i + 1, j + 1, k + 1].score);
					}
					if ((MarchTables.edges[cubeIndex] & 64) == 64)
					{
						edgeVertices[6] = VertexInterpolation(chunk.grid[i + 1, j + 1, k + 1].position, chunk.grid[i, j + 1, k + 1].position, chunk.grid[i + 1, j + 1, k + 1].score, chunk.grid[i, j + 1, k + 1].score);
					}
					if ((MarchTables.edges[cubeIndex] & 128) == 128)
					{
						edgeVertices[7] = VertexInterpolation(chunk.grid[i, j + 1, k + 1].position, chunk.grid[i, j + 1, k].position, chunk.grid[i, j + 1, k + 1].score, chunk.grid[i, j + 1, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 256) == 256)
					{
						edgeVertices[8] = VertexInterpolation(chunk.grid[i, j, k].position, chunk.grid[i, j + 1, k].position, chunk.grid[i, j, k].score, chunk.grid[i, j + 1, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 512) == 512)
					{
						edgeVertices[9] = VertexInterpolation(chunk.grid[i + 1, j, k].position, chunk.grid[i + 1, j + 1, k].position, chunk.grid[i + 1, j, k].score, chunk.grid[i + 1, j + 1, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 1024) == 1024)
					{
						edgeVertices[10] = VertexInterpolation(chunk.grid[i + 1, j, k + 1].position, chunk.grid[i + 1, j + 1, k + 1].position, chunk.grid[i + 1, j, k + 1].score, chunk.grid[i + 1, j + 1, k + 1].score);
					}
					if ((MarchTables.edges[cubeIndex] & 2048) == 2048)
					{
						edgeVertices[11] = VertexInterpolation(chunk.grid[i, j, k + 1].position, chunk.grid[i, j + 1, k + 1].position, chunk.grid[i, j, k + 1].score, chunk.grid[i, j + 1, k + 1].score);
					}
					
					for (int l = 0; MarchTables.triangles[cubeIndex, l] != -1; l += 3)
					{
						vertices[surfaceIndex].Add(edgeVertices[MarchTables.triangles[cubeIndex, l]]);
						vertices[surfaceIndex].Add(edgeVertices[MarchTables.triangles[cubeIndex, l + 1]]);
						vertices[surfaceIndex].Add(edgeVertices[MarchTables.triangles[cubeIndex, l + 2]]);
						Vector3 normal = (vertices[surfaceIndex][vertices[surfaceIndex].Count - 3] - vertices[surfaceIndex][vertices[surfaceIndex].Count - 2]).Cross(vertices[surfaceIndex][vertices[surfaceIndex].Count - 1] - vertices[surfaceIndex][vertices[surfaceIndex].Count - 2]).Normalized();
						normals[surfaceIndex].Add(normal);
						normals[surfaceIndex].Add(normal);
						normals[surfaceIndex].Add(normal);
					}
				}
			}
		}
		GenerateSurfaceData(chunk, vertices, normals);
	}

	void GenerateSurfaceData(Chunk chunk, List<Vector3>[] vertices, List<Vector3>[] normals)
	{
		List<Vector3>[][] surfacesData = new List<Vector3>[surfaceCount][];
		for(int i = 0; i < vertices.Length; i++)
		{
			if (vertices[i] == null)
			{
				continue;
			}
			if (surfacesData[i] == null)
				surfacesData[i] = new List<Vector3>[vertices[i].Count];
			if (surfacesData[i][0] == null)
			{
				surfacesData[i][0] = new List<Vector3>();
				surfacesData[i][1] = new List<Vector3>();
			}
			for(int j = 0; j < vertices[i].Count; j++)
			{
				surfacesData[i][0].Add(vertices[i][j]);
				surfacesData[i][1].Add(normals[i][j]);
			}
		}
		GenerateMesh(chunk, surfacesData);
	}

	void GenerateMesh(Chunk chunk, List<Vector3>[][] surfacesData)
	{
		for(int i = 0; i < surfacesData.Length; i++)
		{
			if (surfacesData[i] == null)
			{
				continue;
			}
			Array arrays = new Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = surfacesData[i][0].ToArray();
			arrays[(int)Mesh.ArrayType.Normal] = surfacesData[i][1].ToArray();
			(chunk.meshInstance.Mesh as ArrayMesh).AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		}
		chunk.mesh = chunk.meshInstance.Mesh;
		/*for(int i = 0; i < surfacesData.Length; i++)
		{
			if (surfacesData[i] == null)
			{
				continue;
			}
			StandardMaterial3D material = new StandardMaterial3D();
			material.AlbedoColor = new Color(GD.Randf(), GD.Randf(), GD.Randf());

			chunk.mesh.SurfaceSetMaterial(i, material);
		}*/
		GenerateCollider(chunk);
	}

	void GenerateCollider(Chunk chunk)
	{
		chunk.collisionShape.Shape = chunk.mesh.CreateTrimeshShape();
	}

	Vector3I GetChunkIndex(Vector3 position)
	{
		return new Vector3I((int)MathF.Floor(position.X / chunkSize), (int)MathF.Floor(position.Y / chunkSize), (int)MathF.Floor(position.Z / chunkSize)) + lowestChunk;
	}

	Vector3I ChunkToIndex(Vector3I chunk)
	{
		return chunk + lowestChunk;
	}

	Vector3I IndexToChunk(Vector3I index)
	{
		return index - lowestChunk;
	}

	Vector3 VertexInterpolation(Vector3 p1, Vector3 p2, float v1, float v2)
	{
		//return p1 + (p2 - p1) * (surfaceValue - v1) / (v2 - v1);
		//GD.Print("Positions: " + p1 + " " + p2 + " Values: "+ v1 + " " + v2 + " Interpolation: " + (surfaceValue - v1) / (v2 - v1));
		return p1.Lerp(p2, (sqrSurfaceValue - v1) / (v2 - v1));
	}
}
