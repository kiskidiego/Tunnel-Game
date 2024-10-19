using Godot;
using System;
using System.Reflection;

public partial class PlayerController : CharacterBody3D
{
	[Export] const float Speed = 5.0f;
	[Export] const float JumpVelocity = 4.5f;
	[Export] float cameraSensitivity = 0.01f;
	[Export] Node3D camRotator;

	// Get the gravity from the project settings to be synced with RigidBody nodes.
	public float gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}
	public override void _Input(InputEvent @event)
	{
		if(@event is InputEventMouseMotion mouseEvent)
		{
			// Rotate the player based on the mouse movement.
			RotateObjectLocal(Vector3.Down, mouseEvent.Relative.X * cameraSensitivity);

			camRotator.Rotation = new Vector3(Mathf.Clamp(camRotator.Rotation.X - mouseEvent.Relative.Y * cameraSensitivity, -Mathf.Pi / 2, Mathf.Pi / 2), 0, 0);
			Transform = Transform.Orthonormalized();
			camRotator.Transform = camRotator.Transform.Orthonormalized();

		}
	}
	public override void _PhysicsProcess(double delta)
	{
		Vector3 velocity = Velocity;

		// Add the gravity.
		if (!IsOnFloor())
			velocity.Y -= gravity * (float)delta;

		// Handle Jump.
		if (Input.IsActionJustPressed("ui_accept") /*&& IsOnFloor()*/)
			velocity.Y = JumpVelocity;

		// Get the input direction and handle the movement/deceleration.
		// As good practice, you should replace UI actions with custom gameplay actions.
		Vector2 inputDir = Input.GetVector("MovementLeft", "MovementRight", "MovementForward", "MovementBackward");
		Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
		if (direction != Vector3.Zero)
		{
			velocity.X = direction.X * Speed;
			velocity.Z = direction.Z * Speed;
		}
		else
		{
			velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
			velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
		}

		Velocity = velocity;
		MoveAndSlide();
	}
}
