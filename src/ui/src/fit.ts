// Какие виджеты показать, когда панель не помещается в отведённую ширину.

/**
 * @param widths          ширина каждого виджета вместе с промежутком после него
 * @param pinned          закреплён ли виджет (статусы и "pin": true) — такие прячутся в последнюю очередь
 * @param available       сколько места есть под виджеты
 * @param fitButtonWidth  ширина кнопки «расширить окно»; она появляется, как только что-то спрятано
 * @returns для каждого виджета — показывать ли его
 *
 * Виджеты прячутся целиком и слева направо: справа стоят самые важные.
 */
export function chooseVisible(widths: number[], pinned: boolean[], available: number, fitButtonWidth: number): boolean[] {
  const visible = widths.map(() => true)
  const total = () => widths.reduce((sum, width, i) => sum + (visible[i] ? width : 0), 0)
  if (total() <= available) return visible

  for (const hidingPinned of [false, true]) {
    for (let i = 0; i < widths.length; i++) {
      if (pinned[i] !== hidingPinned) continue
      if (total() + fitButtonWidth <= available) return visible
      visible[i] = false
    }
  }
  return visible
}
