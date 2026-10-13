import type { ChangeEvent, JSX } from "react";
import { t } from "../../lib/i18n";
import type { BulkOperationResponse } from "../../api/dto";
import type { BulkStatus } from "../../hooks/useBulkOperations";
import { Badge, Card, ErrorBanner, InfoBanner, Spinner } from "../primitives";

const EXAMPLE = `[
  { "operation": "create", "email": "new.user@corp.example.com", "name": "New User", "password": "P@ssw0rd1" },
  { "operation": "update", "email": "existing@corp.example.com", "name": "Updated Name" }
]`;

export interface BulkPanelProps {
  input: string;
  onInputChange: (value: string) => void;
  status: BulkStatus;
  isBusy: boolean;
  notice: string | null;
  error: string | null;
  result: BulkOperationResponse | null;
  onRunSync: () => void;
  onRunAsync: () => void;
}

function BulkResult({ result }: { result: BulkOperationResponse }): JSX.Element {
  return (
    <div>
      <div style={{ display: "flex", gap: "16px", marginBottom: "12px", fontSize: "13.5px" }}>
        <div>
          <Badge tone="success">{t("bulk.completed", { count: result.completedCount })}</Badge>
        </div>
        <div>
          <Badge tone={result.failedCount > 0 ? "danger" : "neutral"}>
            {t("bulk.failed", { count: result.failedCount })}
          </Badge>
        </div>
        {result.jobId !== "" && <div className="text-muted">job {result.jobId}</div>}
      </div>
      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              <th>Email</th>
              <th>{t("common.status")}</th>
              <th>{t("bulk.message")}</th>
            </tr>
          </thead>
          <tbody>
            {result.items.map((item, index) => (
              <tr key={`${item.email}-${index}`}>
                <td className="cell-mono">{item.email}</td>
                <td>
                  {item.status === "completed" ? (
                    <Badge tone="success">{t("common.ok")}</Badge>
                  ) : (
                    <Badge tone="danger">{t("common.failure")}</Badge>
                  )}
                </td>
                <td>{item.message}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/**
 * The "Bulk operations" tab, presentational. Parsing the JSON, the requests and the job polling
 * all live in useBulkOperations; what arrives here is text, state and two handlers.
 */
export function BulkPanel({
  input,
  onInputChange,
  status,
  isBusy,
  notice,
  error,
  result,
  onRunSync,
  onRunAsync,
}: BulkPanelProps): JSX.Element {
  return (
    <Card>
      <Card.Body>
        <p className="text-muted mt-0">{t("bulk.pasteHint")}</p>
        <div className="pattern-output" style={{ marginBottom: "14px" }}>
          {EXAMPLE}
        </div>

        <textarea
          rows={8}
          value={input}
          placeholder='[{"operation":"create","email":"...","name":"...","password":"..."}]'
          onChange={(event: ChangeEvent<HTMLTextAreaElement>) => onInputChange(event.target.value)}
        />

        <div className="form-actions" style={{ justifyContent: "flex-start", marginTop: "12px" }}>
          <button className="btn btn--primary" onClick={onRunSync} disabled={isBusy}>{t("bulk.runNow")}</button>
          <button className="btn" onClick={onRunAsync} disabled={isBusy}>{t("bulk.enqueue")}</button>
        </div>

        <div style={{ marginTop: "16px" }}>
          {status === "running" && <Spinner label={t("bulk.running")} />}
          {notice !== null && <InfoBanner>{notice}</InfoBanner>}
          {error !== null && <ErrorBanner>{error}</ErrorBanner>}
          {result !== null && <BulkResult result={result} />}
        </div>
      </Card.Body>
    </Card>
  );
}
