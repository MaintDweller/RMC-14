namespace Content.Shared._RMC14.Sensor;

[ByRefEvent]
public readonly record struct SensorTowerStateChangedEvent(
    Entity<SensorTowerComponent> Tower,
    SensorTowerState PreviousState,
    EntityUid? User);
