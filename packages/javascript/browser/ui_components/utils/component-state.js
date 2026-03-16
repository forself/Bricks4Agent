function cloneState(value) {
    if (typeof structuredClone === 'function') {
        return structuredClone(value);
    }

    return JSON.parse(JSON.stringify(value));
}

export function createComponentState(initialState, transitions = {}) {
    let currentState = cloneState(initialState);
    const listeners = new Set();

    function snapshot() {
        return cloneState(currentState);
    }

    function can(event) {
        return typeof transitions[event] === 'function';
    }

    function replace(nextState) {
        const previousState = snapshot();
        currentState = cloneState(nextState);
        const nextSnapshot = snapshot();

        for (const listener of listeners) {
            listener(nextSnapshot, previousState, 'REPLACE', null);
        }

        return nextSnapshot;
    }

    function send(event, payload = null) {
        const transition = transitions[event];
        if (typeof transition !== 'function') {
            throw new Error(`Unknown component state event: ${event}`);
        }

        const previousState = snapshot();
        const nextState = transition(snapshot(), payload);

        if (!nextState || typeof nextState !== 'object') {
            throw new Error(`Transition "${event}" must return a state object.`);
        }

        currentState = cloneState(nextState);
        const nextSnapshot = snapshot();

        for (const listener of listeners) {
            listener(nextSnapshot, previousState, event, payload);
        }

        return nextSnapshot;
    }

    function subscribe(listener) {
        listeners.add(listener);
        return () => listeners.delete(listener);
    }

    return {
        snapshot,
        send,
        can,
        replace,
        subscribe
    };
}

export default createComponentState;
