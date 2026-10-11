// SPDX-License-Identifier: GPL-3.0-only
// Local research only. Use a COPY of a saved project; preserve raw exports.
// @category KcdUs
import ghidra.app.script.GhidraScript;
import ghidra.app.cmd.function.CreateFunctionCmd;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.address.Address;
import ghidra.program.model.data.ArrayDataType;
import ghidra.program.model.data.ByteDataType;
import ghidra.program.model.data.DWordDataType;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.pcode.JumpTable;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.RefType;
import ghidra.program.model.symbol.SourceType;
import java.nio.file.*;
import java.util.*;

public class BoundedSwitchRepair extends GhidraScript {
    public void run() throws Exception {
        String[] args=getScriptArgs();
        if(args.length!=3)throw new IllegalArgumentException("TSV, ignored output directory, expected imported SHA256 required");
        Path manifest=Path.of(args[0]),output=Path.of(args[1]);
        if(!Arrays.asList(output.toAbsolutePath().normalize().toString().split("[\\\\/]")).contains("_work"))
            throw new IllegalArgumentException("Private output must stay under _work");
        if(!args[2].equalsIgnoreCase(currentProgram.getExecutableSHA256()))
            throw new IllegalStateException("Imported engine identity differs");
        Files.createDirectories(output);
        List<Function> repaired=new ArrayList<>();
        for(String line:Files.readAllLines(manifest)) {
            if(line.isBlank() || line.startsWith("#"))continue;
            if(repaired.size()>=8)throw new IllegalArgumentException("Eight-function repair budget exceeded");
            String[] row=line.split("\\t");
            if(row.length!=5)throw new IllegalArgumentException("root/branch/table/remap-or-minus/count TSV required");
            Address root=toAddr(row[0]),branch=toAddr(row[1]),table=toAddr(row[2]);
            int count=Integer.parseInt(row[4]);
            if(count<1 || count>256 || table.subtract(root)<0 || table.subtract(root)>20000)
                throw new IllegalArgumentException("Table bounds exceeded");
            Function function=getFunctionAt(root);
            Instruction instruction=getInstructionAt(branch);
            if(function==null || instruction==null || !function.getBody().contains(branch)
                || !instruction.getFlowType().isJump() || !instruction.getFlowType().isComputed())
                throw new IllegalArgumentException("No computed jump in the specified entry");
            ArrayList<Address> destinations=new ArrayList<>();
            for(int i=0;i<count;i++) {
                long offset=Integer.toUnsignedLong(getInt(table.add(i*4)));
                Address destination=currentProgram.getImageBase().add(offset);
                if(destination.compareTo(root)<0 || destination.compareTo(table)>=0)
                    throw new IllegalArgumentException("Destination outside validated code region");
                destinations.add(destination);
            }
            if(!row[3].equals("-")) {
                Address remap=toAddr(row[3]);
                if(!table.add(count*4).equals(remap))throw new IllegalArgumentException("Nonadjacent remap table");
                int max=0;
                for(byte value:getBytes(remap,256))max=Math.max(max,Byte.toUnsignedInt(value));
                if(max+1!=count)throw new IllegalArgumentException("Remap/table counts disagree");
                clearListing(remap,remap.add(255));
                createData(remap,new ArrayDataType(ByteDataType.dataType,256,1));
            }
            clearListing(table,table.add(count*4-1));
            createData(table,new ArrayDataType(DWordDataType.dataType,count,4));
            for(Reference reference:instruction.getReferencesFrom()) {
                if(reference.getReferenceType().isJump())currentProgram.getReferenceManager().delete(reference);
            }
            for(Address destination:destinations)
                instruction.addOperandReference(0,destination,RefType.COMPUTED_JUMP,SourceType.USER_DEFINED);
            new JumpTable(branch,destinations,true,0).writeOverride(function);
            CreateFunctionCmd.fixupFunctionBody(currentProgram,function,monitor);
            repaired.add(function);
            println("COOP-REPAIR root="+root+" destinations="+count+" body="+function.getBody().getNumAddresses());
        }
        DecompInterface decompiler=new DecompInterface();
        decompiler.toggleCCode(true);decompiler.toggleSyntaxTree(false);
        if(!decompiler.openProgram(currentProgram))throw new IllegalStateException(decompiler.getLastMessage());
        try {
            for(Function function:repaired) {
                DecompileResults result=decompiler.decompileFunction(function,30,monitor);
                boolean ok=result.decompileCompleted() && result.getDecompiledFunction()!=null;
                String c=ok?result.getDecompiledFunction().getC():"FAILED: "+result.getErrorMessage();
                Files.writeString(output.resolve(function.getEntryPoint()+".c"),c);
                println("COOP-REPAIR export="+function.getEntryPoint()+" ok="+ok+" error="+result.getErrorMessage());
                decompiler.flushCache();
            }
        } finally {decompiler.dispose();}
        Files.writeString(output.resolve("repair-finished.txt"),"Attempts="+repaired.size()+"; raw exports unchanged; switch labels/signatures still require review\n");
    }
}
