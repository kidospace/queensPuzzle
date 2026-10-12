using System.Collections;
using Coffee.UIExtensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace qp {
    /// <summary>One reusable score celebration. Art and timing are configured on the prefab.</summary>
    public sealed class MBTournamentBonesEffect : MonoBehaviour {
        [SerializeField] Image aura;
        [SerializeField] Image rim;
        [SerializeField] UIParticle motes;
        [SerializeField] float duration = 1.2f;
        [SerializeField] float revealDelay = 0.24f;
        Transform _home;
        Coroutine _play;
        public float RevealDelay => revealDelay;
        public bool IsPlaying => _play != null;

        public void Attach(TMP_Text number) {
            Stop();
            if (_home == null) _home = transform.parent;
            var badge = (RectTransform)number.transform.parent;
            transform.SetParent(badge, false);
            transform.position = badge.TransformPoint(badge.rect.center);
            transform.SetAsFirstSibling();
            if (rim != null) {
                var background = badge.GetComponent<Image>();
                rim.sprite = background.sprite;
                rim.type = background.type;
                rim.pixelsPerUnitMultiplier = background.pixelsPerUnitMultiplier;
                rim.rectTransform.sizeDelta = badge.rect.size + Vector2.one * 4f;
            }
            // The aura is below the digits; motes are a separate sibling above them.
            motes.transform.SetParent(badge, false);
            motes.transform.position = transform.position;
            motes.transform.SetAsLastSibling();
        }

        public void Play() {
            if (_play != null) StopCoroutine(_play);
            gameObject.SetActive(true);
            motes.gameObject.SetActive(true);
            motes.Play();
            _play = StartCoroutine(Animate());
        }

        IEnumerator Animate() {
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime) {
                float p = t / duration;
                float strength = p < 0.3f ? Mathf.SmoothStep(0f, 1f, p / 0.3f)
                    : 1f - Mathf.SmoothStep(0f, 1f, (p - 0.3f) / 0.7f);
                var color = aura.color;
                color.a = strength * 0.85f;
                aura.color = color;
                if (rim != null) rim.color = new Color(1f, 1f, 1f, strength * 0.85f);
                aura.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.12f, Mathf.SmoothStep(0f, 1f, p));
                yield return null;
            }
            _play = null;
            Stop();
        }

        public void Stop() {
            if (_play != null) StopCoroutine(_play);
            _play = null;
            ResetVisuals();
            // Unity forbids reparenting inside a hierarchy that is being deactivated.
            // On the next popup enable (or Attach), Stop parks the inactive pieces safely.
            if (_home != null && _home.gameObject.activeInHierarchy) {
                motes.transform.SetParent(transform, false);
                transform.SetParent(_home, false);
            }
            gameObject.SetActive(false);
        }

        void OnDisable() {
            if (_play != null) StopCoroutine(_play);
            _play = null;
            ResetVisuals();
        }

        void ResetVisuals() {
            if (rim != null) rim.color = new Color(1f, 1f, 1f, 0f);
            if (aura != null) {
                var color = aura.color; color.a = 0f; aura.color = color;
                aura.rectTransform.localScale = Vector3.one;
            }
            if (motes != null) {
                motes.Stop(); motes.Clear();
                motes.gameObject.SetActive(false);
            }
        }
    }
}
