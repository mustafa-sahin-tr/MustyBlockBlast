using System.Runtime.CompilerServices;

// BoardModel's mutators are internal so only Gameplay Systems can change the board. The EditMode test
// assembly needs the same access to arrange a board before exercising a System against it.
[assembly: InternalsVisibleTo("MustyBlockBlast.Tests.EditMode")]
