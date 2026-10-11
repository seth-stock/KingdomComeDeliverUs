// SPDX-License-Identifier: GPL-3.0-only
// @category KcdUs
import ghidra.app.script.GhidraScript;
import java.nio.file.*;

public class ImportCheckpoint extends GhidraScript {
    @Override public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 1) throw new IllegalArgumentException("output directory required");
        Path output = Path.of(args[0]);
        Files.createDirectories(output);
        Files.writeString(output.resolve("import-checkpoint.txt"), currentProgram.getName() + "\n");
        println("COOP-FULL import checkpoint ready; headless must save before analysis");
    }
}
