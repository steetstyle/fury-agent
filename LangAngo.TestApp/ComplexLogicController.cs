namespace LangAngo.TestApp;

public class ComplexLogicController
{
    public object Handle()
    {
        var a = new ServiceA();
        var result = a.Process();
        return new { message = "complex-logic", data = result };
    }
}

public class ServiceA
{
    public string Process()
    {
        var b = new ServiceB();
        return b.GetData();
    }
}

public class ServiceB
{
    public string GetData()
    {
        return "ServiceB result";
    }
}
