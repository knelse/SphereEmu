using Godot;
using System;

public class PlayerDummy : StaticBody
{
	// Declare member variables here. Examples:
	// private int a = 2;
	// private string b = "text";

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

  // Called every frame. 'delta' is the elapsed time since the previous frame.
  public override void _Process(float delta)
  {
	try {
	var savedCoords = System.IO.File.ReadAllText("C:\\source\\clientCoordsSaved").Split("\n");
	var x = Convert.ToDouble(savedCoords[0]) + 1093 + 37;
	var y = - (Convert.ToDouble(savedCoords[1]) - 4503);
	var z = - (Convert.ToDouble(savedCoords[2]) - 1900);
	
	var transform = Transform;
	transform.origin = new Vector3 ((float) x, (float) y, (float) z);
	Transform = transform;
	}
	catch (Exception e) {
		
	}
  }
}
