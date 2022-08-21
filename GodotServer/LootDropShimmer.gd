extends MeshInstance


# Declare member variables here. Examples:
# var a = 2
# var b = "text"
var emissionEnergy : float = 1.0;
var decrease : bool = true;

# Called when the node enters the scene tree for the first time.
func _ready():
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta):
	var material : SpatialMaterial = get_active_material(0)
	if (decrease):
		emissionEnergy -= delta
		if (emissionEnergy < 0):	
			emissionEnergy = 0
			decrease = false
	else:
		emissionEnergy += delta
		if (emissionEnergy) > 1:
			emissionEnergy = 1
			decrease = true
		
	material.emission_energy = emissionEnergy
	mesh.surface_set_material(0, material)
	pass
