using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Array = Godot.Collections.Array;


public partial class MapGenerator : Node3D
{
	partial class Random : RandomNumberGenerator
	{
		public Random() { }
		public Random(ulong seed)
		{
			Seed = seed;
		}
		new public int RandiRange(int min, int max)
		{
			Seed++;
			return base.RandiRange(min, max);
		}
		new public float RandfRange(float min, float max)
		{
			Seed++;
			return base.RandfRange(min, max);
		}
	}
	//TODO: Do something about already rendered chunks (they occupy memory)
	class Point
	{
		public Vector3 position;
		public float score = float.MaxValue;
		public Point(Vector3 position)
		{
			this.position = position;
		}
	}
	class Curve
	{
		public float startRadius;
		public Vector3 startPoint;
		public Vector3 endPoint;
		public Vector3 controlPoint;
		public float endRadius;
	}
	class CurvePoint
	{
		public Vector3 position;
		public float radius;
		public CurvePoint(Vector3 position, float radius)
		{
			this.position = position;
			this.radius = radius;
		}
	}

	[Export] string name;
	[Export] ulong seed = 0;
	[Export] int worldSize = 100;
	[Export] public int chunkSize { get; private set; } = 10;
	[Export] float cubeSize = 1;
	[Export] Vector3 tunnelOrigin;
	[Export] float tunnelRange = 10;
	[Export] int tunnelAmount = 10;
	[Export] int curveSamples = 10;
	[Export] float surfaceValue = 3;
	[Export] float minRadius = 5;
	[Export] float maxRadius = 10;
	[Export] int renderDistance = 5;
	[Export] float branchiness = 0.5f;
	[Export] bool interpolateNormals = true;
	[Export] float heightNoiseIntensity = 20;
	[Export] Noise heightNoise;
	float sqrSurfaceValue;
	List<Vector3I> renderedChunks = new List<Vector3I>();
	Vector3I lowestChunk;
	List<CurvePoint>[,,] chunkCurvePoints;
	bool[,,] loadedChunks;
	MeshInstance3D[,,] chunkMeshes;
	Random random;
	float halfworldwidth;
	public override void _Ready()
	{
		if(seed == 0)
		{
			seed = (ulong)DateTime.Now.Ticks;
		}
		sqrSurfaceValue = surfaceValue * surfaceValue;
		random = new Random(seed);
		halfworldwidth = worldSize * chunkSize * cubeSize / 2;
		////////GD.Print("Lowest chunk: " + lowestChunk);
		SaveMapParameters();
		PrepareChunkGrid();
		SampleCurves(PrepareCurves());
		SaveCurves();
		DoChunkOperations(tunnelOrigin);
	}

	private void SaveMapParameters()
	{
		MapSaveSystem.CreateMap(name, new Vector3I(worldSize, worldSize, worldSize));
	}

	private void SaveCurves()
	{
		for (int i = 0; i < chunkCurvePoints.GetLength(0); i++)
		{
			for (int j = 0; j < chunkCurvePoints.GetLength(1); j++)
			{
				for (int k = 0; k < chunkCurvePoints.GetLength(2); k++)
				{
					if (chunkCurvePoints[i, j, k].Count > 0)
					{
						MapCurvePoint[] curvePoints = new MapCurvePoint[chunkCurvePoints[i, j, k].Count];
						for (int l = 0; l < chunkCurvePoints[i, j, k].Count; l++)
						{
							curvePoints[l] = new MapCurvePoint();
							curvePoints[l].position = chunkCurvePoints[i, j, k][l].position;
							curvePoints[l].radius = chunkCurvePoints[i, j, k][l].radius;
						}
						MapSaveSystem.AddCurvePoints(new Vector3I(i, j, k), curvePoints);
					}
				}
			}
		}
	}

	void PrepareChunkGrid()
	{
		chunkCurvePoints = new List<CurvePoint>[worldSize, worldSize, worldSize];
		chunkMeshes = new MeshInstance3D[worldSize, worldSize, worldSize];
		loadedChunks = new bool[worldSize, worldSize, worldSize];
		for (int i = 0; i < worldSize; i++)
		{
			for (int j = 0; j < worldSize; j++)
			{
				for (int k = 0; k < worldSize; k++)
				{
					chunkCurvePoints[i, j, k] = new List<CurvePoint>();
				}
			}
		}
	}

	public void DoChunkOperations(Vector3 worldPosition)
	{
		Vector3I chunkIndex = ChunkToIndex(worldPosition);
		////GD.Print("Chunk index: " + chunkIndex + " World Position: " + worldPosition);

		foreach(Vector3I chunk in renderedChunks)
		{
			Vector3I distanceVector = (chunk - chunkIndex).Abs();
			if(distanceVector.X >= renderDistance || distanceVector.Y >= renderDistance || distanceVector.Z >= renderDistance)
			{
				UnloadChunk(chunk);
			}
		}

		for(int x = -renderDistance; x <= renderDistance; x++)
		{
			for(int y = -renderDistance; y <= renderDistance; y++)
			{
				for(int z = -renderDistance; z <= renderDistance; z++)
				{
					Vector3I currentChunkIndex = chunkIndex + new Vector3I(x, y, z);
					if (currentChunkIndex.X < 0 || currentChunkIndex.X >= worldSize || currentChunkIndex.Y < 0 || currentChunkIndex.Y >= worldSize || currentChunkIndex.Z < 0 || currentChunkIndex.Z >= worldSize)
					{
						continue;
					}
					MeshInstance3D meshInstance = chunkMeshes[currentChunkIndex.X, currentChunkIndex.Y, currentChunkIndex.Z];
					if (meshInstance != null && chunkMeshes[currentChunkIndex.X, currentChunkIndex.Y, currentChunkIndex.Z].Visible)
					{
						continue;
					}
					////GD.Print("Rendering X: " + x + " Y: " + y + " Z: " + z);
					ProcessChunk(currentChunkIndex);
					renderedChunks.Add(currentChunkIndex);
				}
			}
		}
		for(int i = 0; i < renderedChunks.Count; i++)
		{
			MeshInstance3D meshInstance = chunkMeshes[renderedChunks[i].X, renderedChunks[i].Y, renderedChunks[i].Z];
			if (meshInstance == null || !meshInstance.Visible)
			{
				renderedChunks.RemoveAt(i);
				i--;
			}
		}
	}

	void UnloadChunk(Vector3I chunk)
	{
		MeshInstance3D meshInstance = chunkMeshes[chunk.X, chunk.Y, chunk.Z];
		if (meshInstance != null)
		{
			meshInstance.ProcessMode = ProcessModeEnum.Disabled;
			meshInstance.Visible = false;
			StaticBody3D chunkBody = meshInstance.GetNode<StaticBody3D>("StaticBody");
			chunkBody.SetPhysicsProcess(false);
			chunkBody.GetNode<CollisionShape3D>("CollisionShape").Disabled = true;
		}
	}

	void ProcessChunk(Vector3I chunk)
	{
		MeshInstance3D meshInstance = chunkMeshes[chunk.X, chunk.Y, chunk.Z];
		if(meshInstance != null && loadedChunks[chunk.X, chunk.Y, chunk.Z])
		{
			meshInstance.ProcessMode = ProcessModeEnum.Inherit;
			meshInstance.Visible = true;
			StaticBody3D chunkBody = meshInstance.GetNode<StaticBody3D>("StaticBody");
			chunkBody.SetPhysicsProcess(true);
			chunkBody.GetNode<CollisionShape3D>("CollisionShape").Disabled = false;
		}
		else if (!loadedChunks[chunk.X, chunk.Y, chunk.Z])
		{
			GenerateChunk(chunk);
		}
	}

	void GenerateChunk(Vector3I chunk)
	{
		loadedChunks[chunk.X, chunk.Y, chunk.Z] = true;
		Point[,,] grid = PrepareGrid(chunk);
		//////GD.Print("Grid prepared: " + grid.Length);
		if (AssignScores(chunk, grid))
		{
			SaveGridChunk(chunk, grid);
			//////GD.Print("if passed");
			List<Vector3> vertices = new List<Vector3>();
			List<Vector3> normals = new List<Vector3>();
			MarchingCubesAlgorithm(chunk, grid, sqrSurfaceValue, vertices, normals);
			if (interpolateNormals)
				InterpolateNormals(vertices, normals);
			RemoveExcess(chunk, vertices, normals);
			//DebugNormals(vertices, normals);
			GenerateMesh(chunk, vertices, normals);
		}
	}

	private void SaveGridChunk(Vector3I chunk, Point[,,] grid)
	{
		MapNode[,,] nodes = new MapNode[chunkSize + 3, chunkSize + 3, chunkSize + 3];
		for (int i = 0; i < grid.GetLength(0); i++)
		{
			for (int j = 0; j < grid.GetLength(1); j++)
			{
				for (int k = 0; k < grid.GetLength(2); k++)
				{
					nodes[i, j, k].position = grid[i, j, k].position;
					nodes[i, j, k].score = grid[i, j, k].score;
				}
			}
		}
		//MapSaveSystem.AddNodes(chunk, nodes);
	}

	Curve[] PrepareCurves()
	{
		float worldWidth = worldSize * chunkSize * cubeSize;
		Queue<Vector3> tunnelBranchPoints = new Queue<Vector3>();
		Queue<float> tunnelSurfaceValues = new Queue<float>();
		Curve[] curves = new Curve[tunnelAmount];
		curves[0] = new Curve();
		curves[0].startPoint = tunnelOrigin;
		curves[0].startRadius = random.RandfRange(minRadius, maxRadius);
		curves[0].endRadius = random.RandfRange(minRadius, maxRadius);

		float x;
		do
		{
			x = random.RandfRange(-tunnelRange, tunnelRange);
		} while (x > worldWidth / 2 - 2 * sqrSurfaceValue || x < -worldWidth / 2 + 2 * sqrSurfaceValue);
		float y;
		do
		{
			y = random.RandfRange(-tunnelRange, tunnelRange / 4);
		} while (y > worldWidth / 2 - 2 * sqrSurfaceValue || y < -worldWidth / 2 + 2 * sqrSurfaceValue);
		float z;
		do
		{
			z = random.RandfRange(-tunnelRange, tunnelRange);
		} while (z > worldWidth / 2 - 2 * sqrSurfaceValue || z < -worldWidth / 2 + 2 * sqrSurfaceValue);
		curves[0].endPoint = new Vector3(x, y, z);


		Vector3 midpoint = (curves[0].startPoint + curves[0].endPoint) / 2;
		do
		{
			x = random.RandfRange(-tunnelRange + midpoint.X, tunnelRange + midpoint.X);
		} while (x > worldWidth / 2 - 2 * sqrSurfaceValue || x < -worldWidth / 2 + 2 * sqrSurfaceValue);
		do
		{
			y = random.RandfRange(-tunnelRange + midpoint.Y, tunnelRange + midpoint.Y);
		} while (y > worldWidth / 2 - 2 * sqrSurfaceValue || y < -worldWidth / 2 + 2 * sqrSurfaceValue);
		do
		{
			z = random.RandfRange(-tunnelRange + midpoint.Z, tunnelRange + midpoint.Z);
		} while (z > worldWidth / 2 - 2 * sqrSurfaceValue || z < -worldWidth / 2 + 2 * sqrSurfaceValue);
		curves[0].controlPoint = new Vector3(x, y, z);

		tunnelBranchPoints.Enqueue(curves[0].endPoint);
		tunnelSurfaceValues.Enqueue(curves[0].endRadius);


		for (int i = 1; i < curves.Length; i++)
		{
			while (random.RandfRange(0, 1) > branchiness)
			{
				Vector3 branchPoint = tunnelBranchPoints.Dequeue();
				tunnelBranchPoints.Enqueue(branchPoint);
				float branchSurfaceValue = tunnelSurfaceValues.Dequeue();
				tunnelSurfaceValues.Enqueue(branchSurfaceValue);
			}
			curves[i] = new Curve();
			curves[i].startPoint = tunnelBranchPoints.Dequeue();
			curves[i].startRadius = tunnelSurfaceValues.Dequeue();
			do
			{
				x = random.RandfRange(-tunnelRange + curves[i].startPoint.X, tunnelRange + curves[i].startPoint.X);
			} while (x > worldWidth / 2 - 2 * sqrSurfaceValue || x < -worldWidth / 2 + 2 * sqrSurfaceValue);
			do
			{
				y = random.RandfRange(-tunnelRange + curves[i].startPoint.Y, tunnelRange + curves[i].startPoint.Y);
			} while (y > worldWidth / 2 - 2 * sqrSurfaceValue || y < -worldWidth / 2 + 2 * sqrSurfaceValue);
			do
			{
				z = random.RandfRange(-tunnelRange + curves[i].startPoint.Z, tunnelRange + curves[i].startPoint.Z);
			} while (z > worldWidth / 2 - 2 * sqrSurfaceValue || z < -worldWidth / 2 + 2 * sqrSurfaceValue);
			curves[i].endPoint = new Vector3(x, y, z);
			midpoint = (curves[i].startPoint + curves[i].endPoint) / 2;
			do
			{
				x = random.RandfRange(-tunnelRange + midpoint.X, tunnelRange + midpoint.X);
			} while (x > worldWidth / 2 - 2 * sqrSurfaceValue || x < -worldWidth / 2 + 2 * sqrSurfaceValue);
			do
			{
				y = random.RandfRange(-tunnelRange + midpoint.Y, tunnelRange + midpoint.Y);
			} while (y > worldWidth / 2 - 2 * sqrSurfaceValue || y < -worldWidth / 2 + 2 * sqrSurfaceValue);
			do
			{
				z = random.RandfRange(-tunnelRange + midpoint.Z, tunnelRange + midpoint.Z);
			} while (z > worldWidth / 2 - 2 * sqrSurfaceValue || z < -worldWidth / 2 + 2 * sqrSurfaceValue);
			curves[i].controlPoint = new Vector3(x, y, z);
			curves[i].endRadius = random.RandfRange(minRadius, maxRadius);
			tunnelBranchPoints.Enqueue(curves[i].endPoint);
			tunnelSurfaceValues.Enqueue(curves[i].endRadius);
		}
		////////GD.Print("Curves prepared");
		return curves;
	}

	void SampleCurves(Curve[] curves)
	{
		for (int curve = 0; curve < tunnelAmount; curve++)
		{
			for (short i = 0; i < curveSamples; i++)
			{
				float radius = Mathf.Lerp(curves[curve].startRadius, curves[curve].endRadius, (float)i / curveSamples);
				float t = (float)i / curveSamples;
				Vector3 q0 = curves[curve].startPoint.Lerp(curves[curve].controlPoint, t);
				Vector3 q1 = curves[curve].controlPoint.Lerp(curves[curve].endPoint, t);
				Vector3 point = q0.Lerp(q1, t);
				Vector3I chunk = ChunkToIndex(point);
				//////////GD.Print("Curve: " + curve + " Point: " + point + " Chunk: " + chunk);

				if (chunkCurvePoints[chunk.X, chunk.Y, chunk.Z] == null)
				{
					chunkCurvePoints[chunk.X, chunk.Y, chunk.Z] = new List<CurvePoint>();
				}
				chunkCurvePoints[chunk.X, chunk.Y, chunk.Z].Add(new CurvePoint(q0.Lerp(q1, t), radius));
			}
		}
		////////GD.Print("Curves sampled");
	}

	Point[,,] PrepareGrid(Vector3I chunk)
	{
		//////GD.Print("Prepare grid");
		Point[,,] grid = new Point[chunkSize + 3, chunkSize + 3, chunkSize + 3];
		Vector3 worldPosition = IndexToChunk(chunk);
		//////GD.Print(grid.GetLength(0) + " " + grid.GetLength(1) + " " + grid.GetLength(2));
		for (int i = 0; i < grid.GetLength(0); i++)
		{
			for (int j = 0; j < grid.GetLength(1); j++)
			{
				for (int k = 0; k < grid.GetLength(2); k++)
				{
					grid[i, j, k] = new Point(new Vector3(worldPosition.X + (i-1) * cubeSize, worldPosition.Y + (j-1) * cubeSize, worldPosition.Z + (k - 1) * cubeSize));
				}
			}
		}
		//////GD.Print("Grid prepared at chunk: " + chunk.index);
		return grid;
	}
	
	bool AssignScores(Vector3I chunk, Point[,,] grid)
	{
		//////GD.Print("Assign scores");
		for (int i = 0; i < grid.GetLength(0); i++)
		{
			for (int j = 0; j <	grid.GetLength(1); j++)
			{
				for (int k = 0; k < grid.GetLength(2); k++)
				{
					if (!AssessScore(grid[i, j, k], chunk))
					{
						return false;
					}
				}
			}
		}
		//////GD.Print("Scores assigned at chunk: " + chunk.index);
		return true;
	}

	bool AssessScore(Point point, Vector3I chunk)
	{
		int startIndex = -Mathf.CeilToInt(surfaceValue / (chunkSize * cubeSize) + 1);
		int endIndex = -startIndex;

		for (int i = startIndex; i <= endIndex; i++)
		{
			for(int j = startIndex; j <= endIndex; j++)
			{
				for(int k = startIndex; k <= endIndex; k++)
				{
					if(chunk.X + i < 0 || chunk.X + i >= chunkCurvePoints.GetLength(0) || chunk.Y + j < 0 || chunk.Y + j >= chunkCurvePoints.GetLength(1) || chunk.Z + k < 0 || chunk.Z + k >= chunkCurvePoints.GetLength(2))
					{
						continue;
					}
					if (chunkCurvePoints[chunk.X + i, chunk.Y + j, chunk.Z + k] == null)
					{
						continue;
					}
					for (int l = 0; l < chunkCurvePoints[chunk.X + i, chunk.Y + j, chunk.Z + k].Count; l++)
					{
						float sqrDistance = (point.position - chunkCurvePoints[chunk.X + i, chunk.Y + j, chunk.Z + k][l].position).LengthSquared();
						float modifiedDistance = sqrDistance - chunkCurvePoints[chunk.X + i, chunk.Y + j, chunk.Z + k][l].radius;
						modifiedDistance += heightNoise.GetNoise3D(point.position.X, point.position.Y, point.position.Z) * heightNoiseIntensity;
						modifiedDistance = Math.Min(modifiedDistance, sqrDistance);
						if (modifiedDistance < point.score)
						{
							point.score = modifiedDistance;
						}
					}
				}
			}
		}
		////////GD.Print("Chunk: " + chunk.index + " Point: " + point.position + " Score: " + score);
		if (point.score == float.MaxValue)
		{
			return false;
		}
		return true;
	}

	void MarchingCubesAlgorithm(Vector3I chunk, Point[,,] grid, float surfaceValue, List<Vector3> vertices, List<Vector3> normals)
	{
		//////GD.Print("Marching cubes algorithm");
		for (int i = 0; i < grid.GetLength(0) - 1; i++)
		{
			for (int j = 0; j < grid.GetLength(1) - 1; j++)
			{
				for (int k = 0; k < grid.GetLength(2) - 1; k++)
				{
					byte cubeIndex = 0;
					if (grid[i, j, k].score < surfaceValue) 
						cubeIndex |= 1;
					if (grid[i + 1, j, k].score < surfaceValue) 
						cubeIndex |= 2;
					if (grid[i + 1, j, k + 1].score < surfaceValue) 
						cubeIndex |= 4;
					if (grid[i, j, k + 1].score < surfaceValue) 
						cubeIndex |= 8;
					if (grid[i, j + 1, k].score < surfaceValue) 
						cubeIndex |= 16;
					if (grid[i + 1, j + 1, k].score < surfaceValue) 
						cubeIndex |= 32;
					if (grid[i + 1, j + 1, k + 1].score < surfaceValue) 
						cubeIndex |= 64;
					if (grid[i, j + 1, k + 1].score < surfaceValue) 
						cubeIndex |= 128;

					if (cubeIndex == 0 || cubeIndex == 255) 
						continue;

					////////GD.Print("Cube index: " + cubeIndex);
					
					if (vertices == null)
					{
						vertices = new List<Vector3>();
						normals = new List<Vector3>();
					}

					Vector3[] edgeVertices = new Vector3[12];
					if ((MarchTables.edges[cubeIndex] & 1) == 1)
					{
						edgeVertices[0] = VertexInterpolation(grid[i, j, k].position, grid[i + 1, j, k].position, grid[i, j, k].score, grid[i + 1, j, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 2) == 2)
					{
						edgeVertices[1] = VertexInterpolation(grid[i + 1, j, k].position, grid[i + 1, j, k + 1].position, grid[i + 1, j, k].score, grid[i + 1, j, k + 1].score);
					}
					if ((MarchTables.edges[cubeIndex] & 4) == 4)
					{
						edgeVertices[2] = VertexInterpolation(grid[i + 1, j, k + 1].position, grid[i , j, k + 1].position, grid[i + 1, j, k + 1].score, grid[i, j, k + 1].score);
					}
					if ((MarchTables.edges[cubeIndex] & 8) == 8)
					{
						edgeVertices[3] = VertexInterpolation(grid[i, j, k + 1].position, grid[i, j, k].position, grid[i, j, k + 1].score, grid[i, j, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 16) == 16)
					{
						edgeVertices[4] = VertexInterpolation(grid[i, j + 1, k].position, grid[i + 1, j + 1, k].position, grid[i, j + 1, k].score, grid[i + 1, j + 1, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 32) == 32)
					{
						edgeVertices[5] = VertexInterpolation(grid[i + 1, j + 1, k].position, grid[i + 1, j + 1, k + 1].position, grid[i + 1, j + 1, k].score, grid[i + 1, j + 1, k + 1].score);
					}
					if ((MarchTables.edges[cubeIndex] & 64) == 64)
					{
						edgeVertices[6] = VertexInterpolation(grid[i + 1, j + 1, k + 1].position, grid[i, j + 1, k + 1].position, grid[i + 1, j + 1, k + 1].score, grid[i, j + 1, k + 1].score);
					}
					if ((MarchTables.edges[cubeIndex] & 128) == 128)
					{
						edgeVertices[7] = VertexInterpolation(grid[i, j + 1, k + 1].position, grid[i, j + 1, k].position, grid[i, j + 1, k + 1].score, grid[i, j + 1, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 256) == 256)
					{
						edgeVertices[8] = VertexInterpolation(grid[i, j, k].position, grid[i, j + 1, k].position, grid[i, j, k].score, grid[i, j + 1, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 512) == 512)
					{
						edgeVertices[9] = VertexInterpolation(grid[i + 1, j, k].position, grid[i + 1, j + 1, k].position, grid[i + 1, j, k].score, grid[i + 1, j + 1, k].score);
					}
					if ((MarchTables.edges[cubeIndex] & 1024) == 1024)
					{
						edgeVertices[10] = VertexInterpolation(grid[i + 1, j, k + 1].position, grid[i + 1, j + 1, k + 1].position, grid[i + 1, j, k + 1].score, grid[i + 1, j + 1, k + 1].score);
					}
					if ((MarchTables.edges[cubeIndex] & 2048) == 2048)
					{
						edgeVertices[11] = VertexInterpolation(grid[i, j, k + 1].position, grid[i, j + 1, k + 1].position, grid[i, j, k + 1].score, grid[i, j + 1, k + 1].score);
					}
					
					for (int l = 0; MarchTables.triangles[cubeIndex, l] != -1; l += 3)
					{
						vertices.Add(edgeVertices[MarchTables.triangles[cubeIndex, l]]);
						vertices.Add(edgeVertices[MarchTables.triangles[cubeIndex, l + 1]]);
						vertices.Add(edgeVertices[MarchTables.triangles[cubeIndex, l + 2]]);
						Vector3 normal = (vertices[vertices.Count - 3] - vertices[vertices.Count - 2]).Cross(vertices[vertices.Count - 1] - vertices[vertices.Count - 2]).Normalized();
						normals.Add(normal);
						normals.Add(normal);
						normals.Add(normal);
					}
				}
			}
		}
		////////GD.Print("Marching cubes algorithm done at chunk: " + chunk.index + vertices[0][0]);
	}

	void InterpolateNormals(List<Vector3> vertices, List<Vector3> normals)
	{
		Dictionary<Vector3, Vector3> normalDictionary = new Dictionary<Vector3, Vector3>();
		for (int i = 0; i < vertices.Count; i++)
		{
			if (normalDictionary.ContainsKey(vertices[i]))
			{
				normalDictionary[vertices[i]] = normalDictionary[vertices[i]] + normals[i];
			}
			else
			{
				normalDictionary[vertices[i]] = normals[i];
			}
		}
		for(int i = 0; i < normals.Count; i++)
		{
			normals[i] = normalDictionary[vertices[i]].Normalized();
		}
	}

	private void RemoveExcess(Vector3I chunk, List<Vector3> vertices, List<Vector3> normals)
	{
		Vector3 chunkBorder = IndexToChunk(chunk);
		for (int i = 0; i < vertices.Count; i += 3)
		{
			for (int j = i; j < i + 3; j++)
			{
				if (vertices[j].X < chunkBorder.X || vertices[j].X > chunkBorder.X + chunkSize * cubeSize || vertices[j].Y < chunkBorder.Y || vertices[j].Y > chunkBorder.Y + chunkSize * cubeSize || vertices[j].Z < chunkBorder.Z || vertices[j].Z > chunkBorder.Z + chunkSize * cubeSize)
				{
					vertices.RemoveAt(i);
					normals.RemoveAt(i);
					vertices.RemoveAt(i);
					normals.RemoveAt(i);
					vertices.RemoveAt(i);
					normals.RemoveAt(i);
					i -= 3;
					break;
				}
			}
		}
	}

	void DebugNormals(List<Vector3> vertices, List<Vector3> normals)
	{
		for (int i = 0; i < vertices.Count; i++)
		{
			MeshInstance3D meshInstance = new MeshInstance3D();
			meshInstance.Mesh = new ImmediateMesh();
			ImmediateMesh mesh = meshInstance.Mesh as ImmediateMesh;
			mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, new StandardMaterial3D());
			mesh.SurfaceAddVertex(vertices[i]);
			mesh.SurfaceAddVertex(vertices[i] + normals[i]);
			mesh.SurfaceEnd();
			(mesh.SurfaceGetMaterial(0) as StandardMaterial3D).AlbedoColor = new Color(1, 0, 0);
			AddChild(meshInstance);
		}
	}

	void GenerateMesh(Vector3I chunk, List<Vector3> vertices, List<Vector3> normals)
	{
		if(vertices.Count == 0)
		{
			return;
		}
		////GD.Print("Generate mesh: " + vertices.Count);
		MeshInstance3D meshInstance = new MeshInstance3D();
		meshInstance.Mesh = new ArrayMesh();
		CollisionShape3D collisionShape = new CollisionShape3D();
		collisionShape.Name = "CollisionShape";
		StaticBody3D chunkBody = new StaticBody3D();
		chunkBody.Name = "StaticBody";

		////GD.Print(chunkBody.Name + " " + collisionShape.Name);
		chunkBody.AddChild(collisionShape);
		meshInstance.AddChild(chunkBody);
		chunkMeshes[chunk.X, chunk.Y, chunk.Z] = meshInstance;
		CallDeferred(Node.MethodName.AddChild, meshInstance);
		//AddChild(meshInstance);
		//meshInstance.Owner = this;


		Array arrays = new Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		(meshInstance.Mesh as ArrayMesh).AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

		meshInstance.Mesh.SurfaceSetMaterial(0, new StandardMaterial3D());

		(meshInstance.Mesh.SurfaceGetMaterial(0) as StandardMaterial3D).VertexColorUseAsAlbedo = true;

		collisionShape.Shape = meshInstance.Mesh.CreateTrimeshShape();
		meshInstance.Name = "Chunk " + chunk;
	}

	public Vector3I ChunkToIndex(Vector3 chunk)
	{
		return new Vector3I((int)MathF.Floor((chunk.X + halfworldwidth) / (chunkSize * cubeSize)), (int)MathF.Floor((chunk.Y + halfworldwidth) / (chunkSize * cubeSize)), (int)MathF.Floor((chunk.Z + halfworldwidth) / (chunkSize * cubeSize)));
	}

	Vector3 IndexToChunk(Vector3I index)
	{
		return new Vector3(index.X * (chunkSize * cubeSize) - halfworldwidth, index.Y * (chunkSize * cubeSize) - halfworldwidth, index.Z * (chunkSize * cubeSize) - halfworldwidth);
	}

	Vector3 VertexInterpolation(Vector3 p1, Vector3 p2, float v1, float v2)
	{
		//return p1 + (p2 - p1) * (surfaceValue - v1) / (v2 - v1);
		////////GD.Print("Positions: " + p1 + " " + p2 + " Values: "+ v1 + " " + v2 + " Interpolation: " + (surfaceValue - v1) / (v2 - v1));
		return p1.Lerp(p2, (sqrSurfaceValue - v1) / (v2 - v1));
	}
	
}

