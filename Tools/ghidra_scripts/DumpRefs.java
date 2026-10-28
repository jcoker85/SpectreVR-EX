// Prints the instructions leading up to every reference to the named symbols - handy for Win16
// imports, whose Pascal-convention arguments the decompiler drops.
// Usage (headless): DumpRefs.java <before> <name> [name...]
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.Symbol;

public class DumpRefs extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        int before = Integer.parseInt(args[0]);
        for (int i = 1; i < args.length; i++) {
            for (Symbol s : currentProgram.getSymbolTable().getSymbols(args[i])) {
                for (Reference r : getReferencesTo(s.getAddress())) {
                    Address from = r.getFromAddress();
                    Function fn = getFunctionContaining(from);
                    println("REF " + args[i] + " from " + from + " in " + (fn == null ? "?" : fn.getName()));
                    Instruction ins = getInstructionAt(from);
                    for (int k = 0; k < before && ins != null && ins.getPrevious() != null; k++)
                        ins = ins.getPrevious();
                    for (int k = 0; k <= before && ins != null; k++, ins = ins.getNext())
                        println("    " + ins.getAddress() + "  " + ins);
                }
            }
        }
    }
}
