import type { UserDto, UserRole } from "../../api/dto";
import { t } from "../../lib/i18n";
import type { BadgeTone } from "../primitives";

// Pure presentation functions: no state, no requests — they are easy to check apart from the
// components, and the components need not repeat these decisions in two places.

export function roleTone(role: UserRole): BadgeTone {
  if (role === "Admin") return "accent";
  if (role === "User") return "neutral";
  return "success";
}

// The caption under a name in the list. The email can be empty — the login is shown instead, or
// the line under the name would simply vanish and leave nothing to identify the entry by. The
// marker is there so the login does not read as an address: that very indistinguishability was
// the original problem.
export function subtitleFor(user: UserDto): string {
  if (user.email !== "") return user.email;
  if (user.username !== "") return t("users.loginSuffix", { username: user.username });
  return "—";
}
