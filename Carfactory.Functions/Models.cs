namespace Carfactory.Functions;

public sealed class CarRequest
{
    public string ModelName { get; set; }
    public string Color { get; set; }
    public string InteriorMaterial { get; set; }
}

public sealed class AssemblyInput
{
    public ModelSpecs Specs { get; set; }
    public CarRequest Request { get; set; }
    public Vehicle Vehicle { get; set; }
}

public sealed class ModelSpecs
{
    public int WheelAmount { get; set; }
    public int WindowAmount { get; set; }
}

public sealed class Vehicle
{
    public string ModelName { get; set; }
    public Guid Chassisnumber { get; set; }
    public string Color { get; set; }
    public string InteriorMaterial { get; set; }
}
