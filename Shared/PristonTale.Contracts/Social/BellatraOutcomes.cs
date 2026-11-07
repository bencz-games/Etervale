namespace PristonTale.Contracts.Social;

// How an entrant's last round start or the game's end went for it.
public enum BellatraOutcome : byte
{
    Playing = 0,
    GoesOn = 1,
    Out = 2,
    Cleared = 3
}

public enum BellatraClanStatus : byte
{
    Ok = 0,
    NotChief = 1,
    NotEnoughBalance = 2,
    OutOfReach = 3,
    TooMuchGold = 4,
    Busy = 5,
    Unavailable = 6
}
