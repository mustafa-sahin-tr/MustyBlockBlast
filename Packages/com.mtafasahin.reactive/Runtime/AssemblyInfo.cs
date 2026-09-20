using System.Runtime.CompilerServices;

// ReactiveProperty<T>.Value's setter is internal so only the consuming game's Systems assembly can
// mutate model state (Views may only Subscribe). Grant that access explicitly per consuming project
// instead of making the setter public, which would let Views and other layers write state directly
// and defeat the whole point of routing mutation through Systems.
[assembly: InternalsVisibleTo("MustyBlockBlast.Gameplay")]
[assembly: InternalsVisibleTo("MustyBlockBlast.Tests.EditMode")]
