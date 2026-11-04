using UnityEngine;
using UnityEngine.InputSystem;

public class inventory : MonoBehaviour
{
	private const int SlotCount = 3;
	private const float SlotWidth = 130f;
	private const float SlotHeight = 58f;
	private const float SlotGap = 8f;

	private readonly GameObject[] slots = new GameObject[SlotCount];
	private grab grabController;

	private void Awake()
	{
		grabController = GetComponent<grab>();
	}

	private void Update()
	{
		if (EscMenu.IsPaused || Keyboard.current == null || grabController == null)
		{
			return;
		}

		if (Keyboard.current.eKey.wasPressedThisFrame)
		{
			StoreHeldItem();
		}

		if (Keyboard.current.digit1Key.wasPressedThisFrame)
		{
			SelectSlot(0);
		}
		else if (Keyboard.current.digit2Key.wasPressedThisFrame)
		{
			SelectSlot(1);
		}
		else if (Keyboard.current.digit3Key.wasPressedThisFrame)
		{
			SelectSlot(2);
		}
	}

	private void StoreHeldItem()
	{
		if (!grabController.IsHoldingItem)
		{
			return;
		}

		int emptySlot = FindEmptySlot();
		if (emptySlot < 0)
		{
			return;
		}

		GameObject item = grabController.TakeHeldItem();
		if (item == null)
		{
			return;
		}

		item.SetActive(false);
		slots[emptySlot] = item;
	}

	private void SelectSlot(int slotIndex)
	{
		GameObject selectedItem = slots[slotIndex];
		if (selectedItem == null)
		{
			if (!grabController.IsHoldingItem)
			{
				return;
			}

			GameObject heldItem = grabController.TakeHeldItem();
			if (heldItem != null)
			{
				heldItem.SetActive(false);
				slots[slotIndex] = heldItem;
			}

			return;
		}

		GameObject previouslyHeld = grabController.IsHoldingItem ? grabController.TakeHeldItem() : null;
		slots[slotIndex] = previouslyHeld;
		if (previouslyHeld != null)
		{
			previouslyHeld.SetActive(false);
		}

		selectedItem.SetActive(true);
		Rigidbody body = selectedItem.GetComponent<Rigidbody>();
		if (grabController.TryHoldItem(body))
		{
			return;
		}

		selectedItem.SetActive(false);
		slots[slotIndex] = selectedItem;
		if (previouslyHeld != null)
		{
			previouslyHeld.SetActive(true);
			grabController.TryHoldItem(previouslyHeld.GetComponent<Rigidbody>());
		}
	}

	private int FindEmptySlot()
	{
		for (int i = 0; i < slots.Length; i++)
		{
			if (slots[i] == null)
			{
				return i;
			}
		}

		return -1;
	}

	private void OnGUI()
	{
		if (EscMenu.IsPaused)
		{
			return;
		}

		float totalWidth = SlotCount * SlotWidth + (SlotCount - 1) * SlotGap;
		float startX = (Screen.width - totalWidth) * 0.5f;
		float y = Screen.height - SlotHeight - 20f;

		for (int i = 0; i < slots.Length; i++)
		{
			GameObject item = slots[i];
			string itemName = item != null ? item.name.Replace("(Clone)", "").Trim() : "Empty";
			Rect slotRect = new Rect(startX + i * (SlotWidth + SlotGap), y, SlotWidth, SlotHeight);
			GUI.Box(slotRect, $"{i + 1}\n{itemName}");
		}

		if (grabController != null && grabController.IsHoldingGun)
		{
			Rect crosshairRect = new Rect(Screen.width * 0.5f - 3f, Screen.height * 0.5f - 3f, 6f, 6f);
			GUI.Box(crosshairRect, GUIContent.none);
		}
	}
}
