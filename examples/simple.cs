// This example reads an RDF/XML file on standard input
// and writes it back out in N3 format to standard output.

// Compile with: mcs -r:../bin/SemWeb.dll simple.cs
// Execute with: MONO_PATH=../bin mono simple.exe < test.rdf

using System;
using SemWeb;

public class Simple {
	public static void Main() {
		MemoryStore store = new MemoryStore();
		
		store.Import(new RdfXmlReader(Console.In));
		
		// The 'using' is important because it is necessary
		// to Close or Dispose the writer once writing is
		// complete so that the final statement is closed
		// with a period.
		using (RdfWriter writer = new N3Writer(Console.Out))
			store.Write(writer);
	}
}
