import { useMemo } from "react";
import { getCurrentUser } from "../lib/auth";
import type { SessionUser } from "../lib/auth";
import { useDashboardData } from "../hooks/useDashboardData";
import { DashboardView } from "../components/dashboard/DashboardView";

export function DashboardPage(): JSX.Element {
  const user = useMemo<SessionUser | null>(() => getCurrentUser(), []);
  const { status, data } = useDashboardData();

  return <DashboardView user={user} status={status} data={data} />;
}
