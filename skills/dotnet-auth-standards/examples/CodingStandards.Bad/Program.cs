using Example.Bad;

// LSP: code written against MutableRectangle breaks when handed a MutableSquare.
MutableRectangle r = new MutableSquare();
r.Width = 5;
r.Height = 4;
Console.WriteLine($"Rectangle contract: 5 x 4 = 20, got {r.Area}");
Console.WriteLine(r.Area == 20 ? "PASS (no violation)" : "VIOLATION: Square changed Width when Height was set");

// ISP: calling a method the type cannot honour.
IFatStore store = new ReadOnlyStore();
try { store.Write(1, "a"); } catch (NotSupportedException) { Console.WriteLine("VIOLATION: ReadOnlyStore throws NotSupportedException from IFatStore.Write"); }
