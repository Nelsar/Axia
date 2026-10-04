import { confirmDialog, toast } from "./ui";
import { messageOf } from "./errors";

interface ConfirmAndRunOptions {
  question: string;
  /** What the request returns does not matter: the result is not read here. */
  run: () => Promise<unknown>;
  success: string;
  failure: string;
}

// Confirm → request → toast. Not a hook: there is no state here, and making it one would oblige
// every call to sit at the top level of a component for no reason at all.
// Returns true only if the action actually went through.
export async function confirmAndRun({
  question,
  run,
  success,
  failure,
}: ConfirmAndRunOptions): Promise<boolean> {
  if (!(await confirmDialog(question))) return false;
  try {
    await run();
    toast(success, { type: "success" });
    return true;
  } catch (error: unknown) {
    toast(messageOf(error, failure), { type: "error" });
    return false;
  }
}
