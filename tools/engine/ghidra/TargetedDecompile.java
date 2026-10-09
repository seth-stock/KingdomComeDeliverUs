// SPDX-License-Identifier: GPL-3.0-only
// Targeted, local-only reverse engineering. Do not publish the output/binary.
// @category KcdUs
import ghidra.app.script.GhidraScript;
import ghidra.app.cmd.disassemble.DisassembleCommand;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.SourceType;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

public class TargetedDecompile extends GhidraScript {
    @Override public void run() throws Exception {
        String[] args=getScriptArgs();
        if(args.length!=2)throw new IllegalArgumentException("target TSV and output directory required");
        Path targets=Path.of(args[0]), output=Path.of(args[1]);
        Files.createDirectories(output);
        List<String[]> rows=new ArrayList<>();
        for(String line:Files.readAllLines(targets)) {
            if(line.isBlank() || line.startsWith("#"))continue;
            String[] row=line.split("\\t");
            if(row.length!=3)throw new IllegalArgumentException("name/start/end TSV required");
            Address start=toAddr(row[1]),end=toAddr(row[2]);
            if(end.subtract(start)>20000)throw new IllegalArgumentException("function byte budget exceeded");
            AddressSet body=new AddressSet(start,end);
            new DisassembleCommand(start,body,true).applyTo(currentProgram,monitor);
            Function f=getFunctionAt(start);
            if(f==null) {
                try { f=currentProgram.getFunctionManager().createFunction(row[0],start,body,SourceType.USER_DEFINED); }
                catch(ghidra.program.database.function.OverlappingFunctionException e) {
                    if(!row[0].startsWith("callee_"))throw e;
                    println("SKIP overlapping helper "+row[0]);continue;
                }
            }
            rows.add(row);
        }
        DecompInterface decompiler=new DecompInterface();
        Runtime runtime=Runtime.getRuntime();
        println("KCDUS-MEM heap-used-MiB="+((runtime.totalMemory()-runtime.freeMemory())/1048576)+" heap-max-MiB="+(runtime.maxMemory()/1048576));
        decompiler.toggleCCode(true);decompiler.toggleSyntaxTree(false);
        if(!decompiler.openProgram(currentProgram))throw new IllegalStateException(decompiler.getLastMessage());
        try {
            for(String[] row:rows) {
                monitor.checkCancelled();
                Function f=getFunctionAt(toAddr(row[1]));
                println("KCDUS-DECOMPILE "+row[0]+" "+row[1]);
                DecompileResults result=decompiler.decompileFunction(f,30,monitor);
                String value=result.decompileCompleted() && result.getDecompiledFunction()!=null?
                    result.getDecompiledFunction().getC():"FAILED: "+result.getErrorMessage();
                Files.writeString(output.resolve(row[0]+".c"),value,StandardCharsets.UTF_8);
                decompiler.flushCache();
            }
        } finally { decompiler.dispose(); }
        println("KCDUS-DECOMPILE finished "+rows.size()+" bounded functions");
        println("KCDUS-MEM heap-used-MiB="+((runtime.totalMemory()-runtime.freeMemory())/1048576));
    }
}
