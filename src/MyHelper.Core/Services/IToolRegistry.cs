using Microsoft.Extensions.AI;

namespace MyHelper.Core.Services;

public interface IToolRegistry
{
    void Register(AIFunction function);
    IReadOnlyList<AIFunction> GetAll();
}
