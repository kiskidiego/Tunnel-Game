using Godot;
using System;


public partial class GameManager : Node
{
	[Export] Node3D player;
	[Export] MapGenerator mapGenerator;
	Vector3I initialIndex;
	Vector3I currentIndex;
	public override void _EnterTree()
	{
		initialIndex = mapGenerator.ChunkToIndex(player.Transform.Origin);
	}
	public override void _Process(double delta)
	{
		currentIndex = mapGenerator.ChunkToIndex(player.Transform.Origin);
		if(initialIndex != currentIndex)
		{
			initialIndex = currentIndex;
			mapGenerator.DoChunkOperations(player.Transform.Origin);
		}
	}
}
