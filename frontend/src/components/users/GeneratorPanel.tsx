import type { ChangeEvent } from "react";
import { t } from "../../lib/i18n";
import type { PatternGeneratorResponse } from "../../api/dto";
import type { GeneratorFormValues, GeneratorStatus } from "../../hooks/useUsernameGenerator";
import { Badge, Card, ErrorBanner, FormRow, Spinner } from "../primitives";

export interface GeneratorPanelProps {
  values: GeneratorFormValues;
  onFieldChange: <K extends keyof GeneratorFormValues>(field: K, value: GeneratorFormValues[K]) => void;
  status: GeneratorStatus;
  result: PatternGeneratorResponse | null;
  error: string | null;
  onRun: () => void;
}

function GeneratorResult({ result }: { result: PatternGeneratorResponse }): JSX.Element {
  return (
    <div>
      <div style={{ marginBottom: "8px" }}>
        <Badge tone="accent">{t("generator.valuesCount", { count: result.count })}</Badge>
      </div>
      {result.details !== undefined && result.details !== null ? (
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>{t("common.value")}</th>
                <th>{t("generator.inLdap")}</th>
              </tr>
            </thead>
            <tbody>
              {result.details.map((detail) => (
                <tr key={detail.value}>
                  <td className="cell-mono">{detail.value}</td>
                  <td>
                    {detail.exists ? (
                      <Badge tone="warning">{t("generator.taken")}</Badge>
                    ) : (
                      <Badge tone="success">{t("generator.free")}</Badge>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="pattern-output">{result.items.join("\n")}</div>
      )}
    </div>
  );
}

/** The "Pattern generator" tab, presentational: useUsernameGenerator makes the request. */
export function GeneratorPanel({
  values,
  onFieldChange,
  status,
  result,
  error,
  onRun,
}: GeneratorPanelProps): JSX.Element {
  const onText =
    (field: "pattern" | "rawText" | "regexPattern" | "usernameTemplate" | "maxItems") =>
    (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>): void =>
      onFieldChange(field, event.target.value);

  const onCheckbox =
    (field: "transliterate" | "checkConflicts") =>
    (event: ChangeEvent<HTMLInputElement>): void =>
      onFieldChange(field, event.target.checked);

  return (
    <Card>
      <Card.Body>
        <div className="form-grid">
          <FormRow label={t("generator.mask")}>
            <input type="text" value={values.pattern} placeholder="node-{01..20}" onChange={onText("pattern")} />
          </FormRow>

          <FormRow label={t("generator.rawText")}>
            <textarea
              rows={4}
              value={values.rawText}
              placeholder={t("generator.rawPlaceholder")}
              onChange={onText("rawText")}
            />
          </FormRow>

          <FormRow label={t("generator.regex")}>
            <input
              type="text"
              value={values.regexPattern}
              placeholder="^(?<lastname>\\S+)\\s+(?<firstname>\\S+)$"
              onChange={onText("regexPattern")}
            />
          </FormRow>

          <FormRow label={t("generator.loginTemplate")}>
            <input
              type="text"
              value={values.usernameTemplate}
              placeholder="{firstname}.{lastname}"
              onChange={onText("usernameTemplate")}
            />
          </FormRow>

          <div className="form-row form-row--inline">
            <label className="checkbox">
              <input type="checkbox" checked={values.transliterate} onChange={onCheckbox("transliterate")} />{" "}
              {t("generator.translit")}
            </label>
          </div>

          <div className="form-row form-row--inline">
            <label className="checkbox">
              <input type="checkbox" checked={values.checkConflicts} onChange={onCheckbox("checkConflicts")} />{" "}
              {t("generator.checkConflicts")}
            </label>
          </div>

          <div className="form-row form-row--inline">
            <label className="form-label" style={{ minWidth: "120px" }}>{t("generator.maxItems")}</label>
            <input
              type="number"
              value={values.maxItems}
              style={{ maxWidth: "120px" }}
              onChange={onText("maxItems")}
            />
          </div>

          <div className="form-actions" style={{ justifyContent: "flex-start" }}>
            <button className="btn btn--primary" onClick={onRun} disabled={status === "loading"}>{t("generator.submit")}</button>
          </div>
        </div>

        <div style={{ marginTop: "16px" }}>
          {status === "loading" && <Spinner label={t("generator.running")} />}
          {status === "error" && <ErrorBanner>{error}</ErrorBanner>}
          {status === "ready" && result !== null && <GeneratorResult result={result} />}
        </div>
      </Card.Body>
    </Card>
  );
}
