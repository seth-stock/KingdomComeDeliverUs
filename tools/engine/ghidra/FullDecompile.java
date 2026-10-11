// SPDX-License-Identifier: GPL-3.0-only
// Resumable export of ALL discovered internal functions, not selected targets.
// @category KcdUs
import ghidra.app.util.headless.HeadlessScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.address.*;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.*;
import java.nio.file.*;
import java.nio.channels.FileChannel;
import java.nio.charset.StandardCharsets;
import java.io.*;
import java.util.*;

public class FullDecompile extends HeadlessScript {
    private static final int SHARD_SIZE = 256;
    private static String quote(String s) {
        StringBuilder b = new StringBuilder("\"");
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
                case '"': b.append("\\\""); break;
                case '\\': b.append("\\\\"); break;
                case '\n': b.append("\\n"); break;
                case '\r': b.append("\\r"); break;
                case '\t': b.append("\\t"); break;
                default:
                    if (c < 32) b.append(String.format("\\u%04x", (int)c));
                    else b.append(c);
            }
        }
        return b.append('"').toString();
    }
    private static void durableMove(Path temp, Path destination) throws IOException {
        try (FileChannel channel = FileChannel.open(temp, StandardOpenOption.WRITE)) { channel.force(true); }
        for (int attempt = 0; ; attempt++) {
            try {
                Files.move(temp, destination, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING);
                break;
            } catch (java.nio.file.AccessDeniedException e) {
                if (attempt == 9) throw e;
                try { Thread.sleep(50); }
                catch (InterruptedException interrupted) {
                    Thread.currentThread().interrupt();
                    throw new IOException("Interrupted while committing export", interrupted);
                }
            }
        }
    }
    private static void atomicText(Path target, String text) throws IOException {
        Path temp = target.resolveSibling(target.getFileName() + ".tmp");
        Files.writeString(temp, text, StandardCharsets.UTF_8);
        durableMove(temp, target);
    }
    private void status(Path output, String phase, int total, int attempted, int good, int failed) throws IOException {
        Runtime r = Runtime.getRuntime();
        atomicText(output.resolve("progress.json"), "{\"phase\":" + quote(phase) +
            ",\"total\":" + total + ",\"attempted\":" + attempted + ",\"ok\":" + good +
            ",\"failed\":" + failed + ",\"heap_used_mib\":" + ((r.totalMemory()-r.freeMemory())/1048576) +
            ",\"heap_max_mib\":" + (r.maxMemory()/1048576) + "}\n");
        println("COOP-FULL " + phase + " " + attempted + "/" + total + " ok=" + good + " failed=" + failed);
    }
    private void facts(Path output) throws Exception {
        if (Files.exists(output.resolve("facts-completed.txt"))) return;
        Path temporary = output.resolve("symbols.tsv.tmp");
        try (BufferedWriter writer = Files.newBufferedWriter(temporary, StandardCharsets.UTF_8)) {
            writer.write("address\ttype\tname\n");
            SymbolIterator symbols = currentProgram.getSymbolTable().getAllSymbols(true);
            while (symbols.hasNext()) {
                monitor.checkCancelled();
                Symbol symbol = symbols.next();
                writer.write(symbol.getAddress() + "\t" + symbol.getSymbolType() + "\t" +
                    symbol.getName(true).replace('\t',' ').replace('\n',' ') + "\n");
            }
        }
        durableMove(temporary, output.resolve("symbols.tsv"));
        temporary = output.resolve("strings.jsonl.tmp");
        try (BufferedWriter writer = Files.newBufferedWriter(temporary, StandardCharsets.UTF_8)) {
            DataIterator data = currentProgram.getListing().getDefinedData(true);
            while (data.hasNext()) {
                monitor.checkCancelled();
                Data value = data.next();
                Object decoded = value.getValue();
                if (decoded instanceof String)
                    writer.write("{\"address\":" + quote(value.getAddress().toString()) +
                        ",\"value\":" + quote((String)decoded) + "}\n");
            }
        }
        durableMove(temporary, output.resolve("strings.jsonl"));
        temporary = output.resolve("references.tsv.tmp");
        try (BufferedWriter writer = Files.newBufferedWriter(temporary, StandardCharsets.UTF_8)) {
            writer.write("from\tto\ttype\tcontaining_function\n");
            ReferenceIterator references = currentProgram.getReferenceManager().getReferenceIterator(currentProgram.getMinAddress());
            while (references.hasNext()) {
                monitor.checkCancelled();
                Reference reference = references.next();
                Function function = currentProgram.getFunctionManager().getFunctionContaining(reference.getFromAddress());
                writer.write(reference.getFromAddress() + "\t" + reference.getToAddress() + "\t" +
                    reference.getReferenceType() + "\t" + (function == null ? "" : function.getEntryPoint().toString()) + "\n");
            }
        }
        durableMove(temporary, output.resolve("references.tsv"));
        atomicText(output.resolve("facts-completed.txt"), "Symbols, decoded strings and native references exported.\n");
        println("COOP-FULL symbols, strings and references exported");
    }
    @Override public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 2) throw new IllegalArgumentException("output directory and timeout seconds required");
        Path output = Path.of(args[0]), shards = output.resolve("functions");
        int timeout = Integer.parseInt(args[1]);
        if (timeout < 1 || timeout > 120) throw new IllegalArgumentException("timeout must be 1..120");
        Files.createDirectories(shards);
        if (analysisTimeoutOccurred()) {
            atomicText(output.resolve("analysis-incomplete.txt"), "Headless analysis timed out; no completed-analysis claim.\n");
            throw new IllegalStateException("Full analysis timed out; resume analysis before exporting");
        }
        // First invocation exits immediately after analysis so headless can SAVE
        // the imported program. Export runs in a second CLI invocation against
        // that saved project. A long export must not prevent analysis persistence.
        if (!Files.exists(output.resolve("analysis-completed.txt"))) {
            atomicText(output.resolve("analysis-completed.txt"), "Analysis reached checkpoint without an analysis timeout.\n");
            println("COOP-FULL analysis checkpoint ready; save project before the export invocation");
            return;
        }
        facts(output);
        Set<String> done = new HashSet<>();
        int good = 0, failed = 0, nextShard = 0;
        try (DirectoryStream<Path> paths = Files.newDirectoryStream(shards, "shard-*.jsonl")) {
            for (Path path : paths) {
                nextShard = Math.max(nextShard, Integer.parseInt(path.getFileName().toString().substring(6, 12))+1);
                try (BufferedReader reader = Files.newBufferedReader(path, StandardCharsets.UTF_8)) {
                    for (String line; (line = reader.readLine()) != null;) {
                        String prefix = "{\"address\":\"";
                        if (!line.startsWith(prefix) || !line.endsWith("}"))
                            throw new IOException("Invalid committed shard " + path);
                        String address = line.substring(prefix.length(), line.indexOf('"', prefix.length()));
                        if (!done.add(address)) throw new IOException("Duplicate export address " + address);
                        if (line.contains("\"status\":\"ok\"")) good++; else failed++;
                    }
                }
            }
        }
        AddressSet covered = new AddressSet(), executable = new AddressSet();
        for (MemoryBlock block : currentProgram.getMemory().getBlocks())
            if (block.isExecute()) executable.add(block.getStart(), block.getEnd());
        int total = 0, matchedDone = 0;
        Path inventoryTemp = output.resolve("functions.tsv.tmp");
        try (BufferedWriter inventory = Files.newBufferedWriter(inventoryTemp, StandardCharsets.UTF_8)) {
            inventory.write("address\tname\tbody_bytes\tthunk\n");
            FunctionIterator iterator = currentProgram.getFunctionManager().getFunctions(true);
            while (iterator.hasNext()) {
                Function f = iterator.next();
                if (f.isExternal()) continue;
                total++; covered.add(f.getBody());
                if (done.contains(f.getEntryPoint().toString())) matchedDone++;
                inventory.write(f.getEntryPoint() + "\t" + f.getName().replace('\t',' ') + "\t" + f.getBody().getNumAddresses() + "\t" + f.isThunk() + "\n");
            }
        }
        durableMove(inventoryTemp, output.resolve("functions.tsv"));
        atomicText(output.resolve("discovery.json"), "{\"functions\":" + total +
            ",\"executable_bytes\":" + executable.getNumAddresses() +
            ",\"function_body_executable_bytes\":" + covered.intersect(executable).getNumAddresses() +
            ",\"note\":\"Discovered functions only; executable gaps can contain padding, data or undiscovered code.\"}\n");
        if (matchedDone != done.size()) throw new IOException("Export belongs to a different function inventory");
        status(output, "exporting", total, done.size(), good, failed);
        DecompInterface decompiler = null;
        BufferedWriter writer = null;
        Path temporary = null;
        int count = 0, sinceReset = 0;
        try {
            FunctionIterator iterator = currentProgram.getFunctionManager().getFunctions(true);
            while (iterator.hasNext()) {
                monitor.checkCancelled();
                Function function = iterator.next();
                if (function.isExternal() || done.contains(function.getEntryPoint().toString())) continue;
                if (decompiler == null) {
                    decompiler = new DecompInterface();
                    decompiler.toggleCCode(true); decompiler.toggleSyntaxTree(false);
                    if (!decompiler.openProgram(currentProgram)) throw new IOException(decompiler.getLastMessage());
                }
                if (writer == null) {
                    temporary = shards.resolve(String.format("shard-%06d.jsonl.tmp", nextShard));
                    writer = Files.newBufferedWriter(temporary, StandardCharsets.UTF_8);
                }
                long started = System.nanoTime();
                DecompileResults result = decompiler.decompileFunction(function, timeout, monitor);
                boolean ok = result.decompileCompleted() && result.getDecompiledFunction() != null;
                if (!ok && timeout < 30) {
                    // Slow functions get a bounded second attempt; never hide failure.
                    monitor.checkCancelled();
                    decompiler.flushCache();
                    result = decompiler.decompileFunction(function, 30, monitor);
                    ok = result.decompileCompleted() && result.getDecompiledFunction() != null;
                }
                String c = ok ? result.getDecompiledFunction().getC() : "";
                String error = result.getErrorMessage();
                writer.write("{\"address\":" + quote(function.getEntryPoint().toString()) +
                    ",\"name\":" + quote(function.getName()) + ",\"status\":" + quote(ok ? "ok" : "failed") +
                    ",\"body_bytes\":" + function.getBody().getNumAddresses() +
                    ",\"seconds\":" + ((System.nanoTime()-started)/1000000000.0) +
                    ",\"error\":" + quote(error == null ? "" : error) + ",\"c\":" + quote(c) + "}\n");
                done.add(function.getEntryPoint().toString());
                if (ok) good++; else failed++;
                count++; sinceReset++;
                if (count == SHARD_SIZE) {
                    writer.close(); writer = null;
                    durableMove(temporary, shards.resolve(String.format("shard-%06d.jsonl", nextShard++)));
                    count = 0; status(output, "exporting", total, done.size(), good, failed);
                }
                if (sinceReset % 64 == 0) decompiler.flushCache();
                if (sinceReset == 1024) { decompiler.dispose(); decompiler = null; sinceReset = 0; }
            }
            if (writer != null) {
                writer.close(); writer = null;
                durableMove(temporary, shards.resolve(String.format("shard-%06d.jsonl", nextShard)));
            }
            status(output, "attempted-all-discovered", total, done.size(), good, failed);
            // Failed functions are explicit work remaining, never silently reported as decompiled.
            atomicText(output.resolve("export-completed.txt"), "COOP-FULL attempted=" + done.size() + " total=" + total + " ok=" + good + " failed=" + failed + "\n");
        } finally {
            if (writer != null) writer.close();
            if (decompiler != null) decompiler.dispose();
        }
    }
}
