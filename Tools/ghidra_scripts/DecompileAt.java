// Decompiles the functions at the given addresses (creating them if Ghidra hasn't), and lists
// instructions in an optional range. Usage: DecompileAt.java <out file> <seg:off>... [list:<seg:off>-<seg:off>]
import java.io.File;
import java.io.PrintWriter;

import ghidra.app.cmd.disassemble.DisassembleCommand;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;

public class DecompileAt extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        try (PrintWriter out = new PrintWriter(new File(args[0]), "UTF-8")) {
            for (int i = 1; i < args.length; i++) {
                if (args[i].startsWith("list:")) {
                    String[] r = args[i].substring(5).split("-");
                    Address a = toAddr(r[0]), b = toAddr(r[1]);
                    new DisassembleCommand(a, new AddressSet(a, b), true).applyTo(currentProgram);
                    out.println("list " + a + " " + b + " " + getInstructionContaining(a));
                    for (Instruction ins = getInstructionContaining(a); ins != null && ins.getAddress().compareTo(b) <= 0; ins = ins.getNext())
                        out.println(ins.getAddress() + "  " + ins);
                    continue;
                }
                Address a = toAddr(args[i]);
                Function f = getFunctionAt(a);
                if (f == null) {
                    disassemble(a);
                    f = createFunction(a, null);
                }
                if (f == null) { out.println("// no function at " + a); continue; }
                DecompileResults res = dec.decompileFunction(f, 60, monitor);
                out.println("// ==== " + f.getName() + " @ " + f.getEntryPoint());
                out.println(res != null && res.decompileCompleted() ? res.getDecompiledFunction().getC() : "// decompile failed");
            }
        }
        dec.dispose();
    }
}
