using DiagramGenerator.Application.Models;
using DiagramGenerator.Domain.Models;

namespace DiagramGenerator.Application.Interfaces;

public interface IMermaidRenderer
{
    string Render(DiagramModel diagram, MermaidRenderOptions options);
}
