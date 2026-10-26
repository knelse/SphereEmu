namespace SphServer.Server.Simulation;

public enum ConnectionCommand : ushort
{
	CharacterScreenPoll = 1,
	ConfirmRoster = 4,
	WorldReady = 6,
	CreateCharacter = 7,
	SubmitCredentials = 10,
	SelectSlot = 12,
	DeleteCharacter = 13,
	NameCheck = 14,
	EnterWorld = 15,
	UseObject = 16,
	Teleport = 17,
}
