export function open(dialog, component) {
    if (!dialog.open) dialog.showModal();
    dialog.addEventListener('cancel', event => {
        event.preventDefault();
        component.invokeMethodAsync('DismissDialog');
    }, { once: true });
    dialog.querySelector('.task-title-input')?.focus();
}
