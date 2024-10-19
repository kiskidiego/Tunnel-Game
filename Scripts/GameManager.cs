using Godot;
using System;

public partial class GameManager : Node
{
	[Export] PlayerController player;
	[Export] MapGenerator mapGenerator;
	Vector3 playerPosition;
	Vector3 chunkPosition;
	public override void _EnterTree()
	{
		chunkPosition = player.Transform.Origin;
	}
	public override void _Process(double delta)
	{
		if(chunkPosition.DistanceSquaredTo(player.Transform.Origin) > mapGenerator.chunkSize * mapGenerator.chunkSize)
		{
			GD.Print("Player position: ", player.Transform.Origin, " Chunk position: ", chunkPosition);
			chunkPosition = player.Transform.Origin;
			mapGenerator.DoChunkOperations(chunkPosition);
		}
	}
}
