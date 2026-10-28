// Writes every function's decompiled C, plus a symbol index, next to the Ghidra project.
// Usage (headless post-script): DumpDecompiled.java <output folder>
import java.io.File;
import java.io.PrintWriter;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;

public class DumpDecompiled extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        File outDir = new File(args.length > 0 ? args[0] : ".");
        outDir.mkdirs();
        String base = currentProgram.getName();

        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);

        int count = 0;
        try (PrintWriter c = new PrintWriter(new File(outDir, base + ".c"), "UTF-8");
             PrintWriter idx = new PrintWriter(new File(outDir, base + ".functions.txt"), "UTF-8")) {
            for (Function f : currentProgram.getFunctionManager().getFunctions(true)) {
                if (monitor.isCancelled()) break;
                idx.println(f.getEntryPoint() + "\t" + f.getBody().getNumAddresses() + "\t" + f.getName());
                DecompileResults r = dec.decompileFunction(f, 60, monitor);
                c.println("// ==== " + f.getName() + " @ " + f.getEntryPoint());
                if (r != null && r.decompileCompleted())
                    c.println(r.getDecompiledFunction().getC());
                else
                    c.println("// decompile failed: " + (r == null ? "null" : r.getErrorMessage()));
                count++;
            }
        }
        dec.dispose();
        println("DumpDecompiled: " + count + " functions -> " + outDir.getAbsolutePath());
    }
}
