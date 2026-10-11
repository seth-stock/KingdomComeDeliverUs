// SPDX-License-Identifier: GPL-3.0-only
// Local engine research only. Never distribute game binaries or decompiler output.
// @category KcdUs
import ghidra.app.script.GhidraScript;
import java.nio.file.*;
import java.util.*;

public class FullAnalysisConfig extends GhidraScript {
    @Override public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 1) throw new IllegalArgumentException("output directory required");
        Path output = Path.of(args[0]);
        Files.createDirectories(output);
        // Offline analysis: no symbol server requests or execution of the input.
        Map<String, String> options = getCurrentAnalysisOptionsAndValues(currentProgram);
        for (String name : options.keySet()) {
            if (name.equals("PDB") || name.equals("PDB Universal") || name.equals("Create Address Tables"))
                setAnalysisOption(currentProgram, name, "false");
        }
        Map<String, String> actual = getCurrentAnalysisOptionsAndValues(currentProgram);
        StringBuilder text = new StringBuilder();
        new TreeMap<>(actual).forEach((name, value) -> text.append(name).append('\t').append(value).append('\n'));
        Files.writeString(output.resolve("analysis-options.tsv"), text);
        println("COOP-FULL analysis configuration recorded; discovery/RTTI/references retained; whole-image address-table heuristic disabled after bounded-heap failure");
    }
}
