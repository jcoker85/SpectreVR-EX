// Applies the Borland debug symbols dumped by tds_symbols.py as labels (renaming functions that
// start at a symbol). NE logical segment N is the Nth memory block in address order that isn't
// an import/header block, so the script prints its mapping for checking.
// Usage (headless): ApplyTdsSymbols.java <symbols.tsv>
import java.io.BufferedReader;
import java.io.FileReader;
import java.util.ArrayList;
import java.util.List;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.SourceType;

public class ApplyTdsSymbols extends GhidraScript {
    @Override
    public void run() throws Exception {
        List<MemoryBlock> segs = new ArrayList<>();
        for (MemoryBlock b : currentProgram.getMemory().getBlocks()) {
            println("block " + b.getName() + " @ " + b.getStart() + " len " + b.getSize());
            if (b.isInitialized() || b.getName().startsWith("Data") || b.getName().startsWith("Code"))
                segs.add(b);
        }

        int labels = 0, renamed = 0, skipped = 0;
        try (BufferedReader r = new BufferedReader(new FileReader(getScriptArgs()[0]))) {
            String line;
            while ((line = r.readLine()) != null) {
                String[] f = line.split("\t");
                int seg = Integer.parseInt(f[0], 16), off = Integer.parseInt(f[1], 16);
                String name = f[4];
                if (seg < 1 || seg > segs.size()) { skipped++; continue; }
                MemoryBlock b = segs.get(seg - 1);
                if (off >= b.getSize()) { skipped++; continue; }
                Address a = b.getStart().add(off);
                createLabel(a, name, true, SourceType.IMPORTED);
                labels++;
                Function fn = getFunctionAt(a);
                if (fn != null) { fn.setName(name, SourceType.IMPORTED); renamed++; }
            }
        }
        println("ApplyTdsSymbols: " + labels + " labels, " + renamed + " functions renamed, " + skipped + " skipped");
    }
}
