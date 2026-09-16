using System;
using System.Collections.Generic;

namespace VisualStudioAgent.Abstractions;

public interface ITodoManager
{
    IReadOnlyList<string> Todos { get; }
    event Action<IReadOnlyList<string>>? TodosChanged;
    string Update(string[]? todos);
}
