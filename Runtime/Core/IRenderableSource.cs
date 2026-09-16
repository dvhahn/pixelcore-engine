using System.Collections.Generic;

namespace PixelCore.Runtime.Core;

public interface IRenderableSource
{
    IReadOnlyList<IRenderable> Renderables { get; }
}
