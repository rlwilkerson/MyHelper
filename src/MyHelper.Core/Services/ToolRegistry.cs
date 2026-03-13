using Microsoft.Extensions.AI;

namespace MyHelper.Core.Services;

public sealed class ToolRegistry : IToolRegistry
{
    private readonly List<AIFunction> _tools = [];

    public void Register(AIFunction function) => _tools.Add(function);

    public IReadOnlyList<AIFunction> GetAll() => _tools.AsReadOnly();
}
