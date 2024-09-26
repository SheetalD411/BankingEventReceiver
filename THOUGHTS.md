What things did you considered of during the implementation?

1. Error handling
2. Logging - Applications like this need substantial logging so that we are clear.
3. Transactions table was needed as it maitains details of every record that happens.
4. Simple things like using enums instead of direct string comparison improve performance.
5. Had to specify clear types when using decimal to take care of warnings.
6. Separation of concerns - trying to follow the Single Responsibility principle such that every method is responsible for a single functionality.
7. Using of transactions is important for maintaining data integrity.
8. Make sure validations happen for non-transient errors and retry in case of transient errors.

Anything was unclear?
if IServiceBusReceiver was supposed to be implemented.